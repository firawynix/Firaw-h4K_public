using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using WpfImage = System.Windows.Controls.Image;

namespace FirawH4K;

// Neutral optical distortion shared by both fight modes. The capture stays
// anchored to the contact point and refreshes off the UI thread.
public partial class FightEffectWindow : Window
{
    private const int EffectMargin = 32;
    private readonly List<(WpfImage Image, TranslateTransform Offset)> strips = [];
    private readonly TranslateTransform echoOffset = new();
    private WpfImage? echo;
    private WriteableBitmap? liveFrame;
    private CancellationTokenSource? captureCancellation;
    private long startedAt;
    private double duration;
    private int generation;
    private bool running;
    private bool hasFrame;

    public FightEffectWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => StopEffect();
        FightCanvas.Effect = new BlurEffect { Radius = 2.8, RenderingBias = RenderingBias.Performance };
        FightCanvas.OpacityMask = new RadialGradientBrush(new GradientStopCollection
        {
            new(Colors.White, 0),
            new(Colors.White, .55),
            new(Colors.Transparent, 1)
        });
    }

    public void PlayAt(double x, double y, Drawing.Rectangle screen, double seconds = 2.4)
    {
        StopEffect();
        var region = Drawing.Rectangle.Intersect(screen,
            new Drawing.Rectangle((int)x - 230, (int)y - 150, 460, 300));
        if (region.Width < 90 || region.Height < 90) return;
        duration = Math.Max(.3, seconds);
        Left = region.Left - EffectMargin;
        Top = region.Top - EffectMargin;
        Width = region.Width + EffectMargin * 2;
        Height = region.Height + EffectMargin * 2;
        FightCanvas.Width = Width;
        FightCanvas.Height = Height;
        FightCanvas.Opacity = 0;

        // GDI captures need Bgr32: their unused fourth byte is not an alpha
        // channel. Treating it as Pbgra32 can make the actual screen disappear.
        liveFrame = new WriteableBitmap(region.Width, region.Height, 96, 96, PixelFormats.Bgr32, null);
        echo = new WpfImage
        {
            Source = liveFrame, Width = region.Width, Height = region.Height,
            Opacity = .24, RenderTransform = echoOffset, IsHitTestVisible = false
        };
        Canvas.SetLeft(echo, EffectMargin);
        Canvas.SetTop(echo, EffectMargin);
        FightCanvas.Children.Add(echo);
        const int rows = 12;
        for (var i = 0; i < rows; i++)
        {
            var top = region.Height * i / (double)rows;
            var bottom = region.Height * (i + 1) / (double)rows;
            var transform = new TranslateTransform();
            var slice = new WpfImage
            {
                Source = liveFrame, Width = region.Width, Height = region.Height,
                Stretch = Stretch.Fill, Opacity = .82,
                Clip = new RectangleGeometry(new Rect(0, top, region.Width, bottom - top + .5)),
                RenderTransform = transform, IsHitTestVisible = false
            };
            Canvas.SetLeft(slice, EffectMargin);
            Canvas.SetTop(slice, EffectMargin);
            FightCanvas.Children.Add(slice);
            strips.Add((slice, transform));
        }

        running = true;
        hasFrame = false;
        startedAt = Stopwatch.GetTimestamp();
        var currentGeneration = ++generation;
        captureCancellation = new CancellationTokenSource();
        if (!IsVisible) Show();
        CompositionTarget.Rendering += Animate;
        _ = CaptureLoopAsync(region, currentGeneration, captureCancellation.Token);
    }

    public void StopEffect()
    {
        running = false;
        generation++;
        CompositionTarget.Rendering -= Animate;
        captureCancellation?.Cancel();
        captureCancellation?.Dispose();
        captureCancellation = null;
        if (IsVisible) Hide();
        FightCanvas.Children.Clear();
        strips.Clear();
        liveFrame = null;
        echo = null;
        hasFrame = false;
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (!running) return;
        var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
        if (elapsed >= duration) { StopEffect(); return; }
        if (!hasFrame) return;
        var envelope = Math.Min(1, elapsed / .14) * Math.Min(1, (duration - elapsed) / .65);
        for (var i = 0; i < strips.Count; i++)
        {
            var offset = strips[i].Offset;
            offset.X = (10 + i % 4 * 4) * Math.Sin(elapsed * 13 + i * 1.35) * envelope;
            offset.Y = 3.5 * Math.Sin(elapsed * 9 + i * .8) * envelope;
        }
        echoOffset.X = 15 * Math.Sin(elapsed * 7) * envelope;
        echoOffset.Y = -6 * Math.Cos(elapsed * 8) * envelope;
        FightCanvas.Opacity = envelope;
    }

    private async Task CaptureLoopAsync(Drawing.Rectangle region, int currentGeneration, CancellationToken token)
    {
        try
        {
            await Task.Run(async () =>
            {
                using var bitmap = new Drawing.Bitmap(region.Width, region.Height, Drawing.Imaging.PixelFormat.Format32bppRgb);
                using var graphics = Drawing.Graphics.FromImage(bitmap);
                var bounds = new Drawing.Rectangle(0, 0, region.Width, region.Height);
                var stride = region.Width * 4;
                var pixels = new byte[stride * region.Height];
                while (!token.IsCancellationRequested)
                {
                    var frameStart = Stopwatch.GetTimestamp();
                    graphics.CopyFromScreen(region.Location, Drawing.Point.Empty, region.Size,
                        Drawing.CopyPixelOperation.SourceCopy);
                    var bits = bitmap.LockBits(bounds, Drawing.Imaging.ImageLockMode.ReadOnly,
                        Drawing.Imaging.PixelFormat.Format32bppRgb);
                    try { Marshal.Copy(bits.Scan0, pixels, 0, pixels.Length); }
                    finally { bitmap.UnlockBits(bits); }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!running || generation != currentGeneration || liveFrame == null) return;
                        liveFrame.WritePixels(new Int32Rect(0, 0, region.Width, region.Height), pixels, stride, 0);
                        hasFrame = true;
                    }, DispatcherPriority.Background, token);
                    var remaining = 33 - Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
                    if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), token);
                }
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Protected capture surfaces should not leave a stale frozen tile.
            if (running && generation == currentGeneration) StopEffect();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLong(handle, -20);
        SetWindowLong(handle, -20, styles | 0x00000020 | 0x00000080 | 0x08000000);
        HwndSource.FromHwnd(handle)?.AddHook((IntPtr _, int message, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (message != 0x0084) return IntPtr.Zero;
            handled = true;
            return new IntPtr(-1);
        });
    }

    [DllImport("user32.dll")] private static extern nint GetWindowLong(nint handle, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLong(nint handle, int index, nint value);
}
