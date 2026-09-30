using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Drawing = System.Drawing;
using WpfImage = System.Windows.Controls.Image;
using MediaColor = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FirawH4K;

// A small, click-through collision effect. Nothing here is a screenshot:
// animated wallpaper, applications and the desktop keep rendering behind it.
public partial class LiveFightWindow : Window
{
    private const int CaptureWidth = 200;
    private const int CaptureHeight = 140;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Ellipse[] waves = new Ellipse[2];
    private readonly Polyline[] shears = new Polyline[4];
    private readonly WpfImage[] liveStrips = new WpfImage[5];
    private readonly WriteableBitmap liveFrame = new(CaptureWidth, CaptureHeight, 96, 96, PixelFormats.Pbgra32, null);
    private readonly Drawing.Bitmap capture = new(CaptureWidth, CaptureHeight, Drawing.Imaging.PixelFormat.Format32bppPArgb);
    private readonly Drawing.Graphics captureGraphics;
    private readonly ScaleTransform rimScale = new();
    private readonly RotateTransform rimRotation = new();
    private DateTime startedAt;
    private DateTime lastPulseAt;
    private DateTime lastCaptureAt;
    private bool running;
    private bool captureFailed;

    public LiveFightWindow()
    {
        InitializeComponent();
        captureGraphics = Drawing.Graphics.FromImage(capture);
        var source = new BitmapImage(new Uri("pack://application:,,,/Assets/abyss-impact-v3.png"));
        source.Freeze();
        Rim.Source = source;
        Rim.CacheMode = new BitmapCache();
        Rim.RenderTransform = new TransformGroup { Children = { rimScale, rimRotation } };
        for (var i = 0; i < liveStrips.Length; i++)
        {
            liveStrips[i] = new WpfImage
            {
                Source = liveFrame,
                Width = CaptureWidth,
                Height = CaptureHeight,
                Stretch = Stretch.Fill,
                Opacity = .38,
                Clip = new RectangleGeometry(new Rect(0, i * 28, CaptureWidth, 28)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(liveStrips[i], 70);
            Canvas.SetTop(liveStrips[i], 100);
            EffectCanvas.Children.Add(liveStrips[i]);
        }
        for (var i = 0; i < shears.Length; i++)
        {
            var red = i % 2 == 0;
            shears[i] = new Polyline
            {
                Stroke = new SolidColorBrush(red
                    ? MediaColor.FromArgb(145, 245, 34, 62)
                    : MediaColor.FromArgb(145, 35, 236, 126)),
                StrokeThickness = i % 2 == 0 ? 2.2 : 1.5,
                StrokeLineJoin = PenLineJoin.Bevel,
                IsHitTestVisible = false
            };
            EffectCanvas.Children.Add(shears[i]);
        }
        for (var i = 0; i < waves.Length; i++)
        {
            waves[i] = new Ellipse
            {
                Stroke = new SolidColorBrush(i == 0
                    ? MediaColor.FromArgb(158, 225, 34, 62)
                    : MediaColor.FromArgb(158, 37, 235, 132)),
                StrokeThickness = 2.2,
                Fill = System.Windows.Media.Brushes.Transparent,
                IsHitTestVisible = false
            };
            EffectCanvas.Children.Add(waves[i]);
        }
        Canvas.SetZIndex(Rim, 1);
        timer.Tick += (_, _) => Animate();
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => { captureGraphics.Dispose(); capture.Dispose(); };
    }

    public void StartAt(double x, double y)
    {
        TrackAt(x, y);
        startedAt = lastPulseAt = DateTime.UtcNow;
        lastCaptureAt = DateTime.MinValue;
        captureFailed = false;
        running = true;
        CaptureLivePixels();
        if (!IsVisible) Show();
        timer.Start();
    }

    public void TrackAt(double x, double y)
    {
        var desiredLeft = x - 170;
        var desiredTop = y - 170;
        if (double.IsNaN(Left) || Math.Abs(Left - desiredLeft) > 1) Left = desiredLeft;
        if (double.IsNaN(Top) || Math.Abs(Top - desiredTop) > 1) Top = desiredTop;
    }

    public void PulseAt(double x, double y)
    {
        TrackAt(x, y);
        lastPulseAt = DateTime.UtcNow;
    }

    public void StopEffect()
    {
        running = false;
        timer.Stop();
        if (IsVisible) Hide();
    }

    private void Animate()
    {
        if (!running) return;
        var now = DateTime.UtcNow;
        var t = (now - startedAt).TotalSeconds;
        var impactAge = (now - lastPulseAt).TotalSeconds;
        var kick = Math.Exp(-impactAge * 4.6);
        var beat = Math.Sin(t * 19);
        if (!captureFailed && (now - lastCaptureAt).TotalMilliseconds >= 85)
            CaptureLivePixels();
        for (var i = 0; i < liveStrips.Length; i++)
        {
            var shear = Math.Sin(t * 15 + i * 1.4) * (3 + kick * 15);
            Canvas.SetLeft(liveStrips[i], 70 + shear);
            liveStrips[i].Opacity = .19 + .29 * kick;
        }
        var scale = .85 + .035 * beat + .11 * kick;
        rimScale.ScaleX = scale;
        rimScale.ScaleY = scale - .025 * beat;
        rimRotation.Angle = 3.5 * Math.Sin(t * 7.5);
        Rim.Opacity = .81 + .18 * kick;

        for (var i = 0; i < shears.Length; i++)
        {
            var y = 125 + i * 27 + Math.Sin(t * 10 + i) * 8;
            var drift = Math.Sin(t * 14 + i * 1.8) * (8 + kick * 13);
            shears[i].Points = new PointCollection
            {
                new(70 + drift, y),
                new(118 + drift, y - 3),
                new(147 + drift, y + 5),
                new(181 + drift, y - 4),
                new(221 + drift, y + 2),
                new(267 + drift, y)
            };
            shears[i].Opacity = (.10 + .27 * kick) * Math.Abs(Math.Sin(t * 13 + i));
        }
        for (var i = 0; i < waves.Length; i++)
        {
            var progress = (impactAge * 1.8 + i * .5) % 1;
            var size = 76 + progress * 206;
            waves[i].Width = size;
            waves[i].Height = size * .72;
            Canvas.SetLeft(waves[i], 170 - size / 2);
            Canvas.SetTop(waves[i], 170 - size * .36);
            waves[i].Opacity = .23 * (1 - progress) * (.35 + .65 * kick);
        }
    }

    private void CaptureLivePixels()
    {
        try
        {
            // SourceCopy omits layered overlays; CAPTUREBLT would re-capture
            // this effect and create an expensive recursive feedback loop.
            captureGraphics.CopyFromScreen((int)Left + 70, (int)Top + 100,
                0, 0, new Drawing.Size(CaptureWidth, CaptureHeight),
                Drawing.CopyPixelOperation.SourceCopy);
            var bounds = new Drawing.Rectangle(0, 0, CaptureWidth, CaptureHeight);
            var bits = capture.LockBits(bounds, Drawing.Imaging.ImageLockMode.ReadOnly,
                Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                liveFrame.WritePixels(new Int32Rect(0, 0, CaptureWidth, CaptureHeight),
                    bits.Scan0, bits.Stride * CaptureHeight, bits.Stride);
            }
            finally { capture.UnlockBits(bits); }
            lastCaptureAt = DateTime.UtcNow;
        }
        catch (Exception)
        {
            // Protected desktop/video surfaces can reject GDI capture. The
            // transparent vector refraction continues without a frozen frame.
            captureFailed = true;
            foreach (var strip in liveStrips) strip.Opacity = 0;
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
