using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using WpfImage = System.Windows.Controls.Image;

namespace FirawH4K;

// One bounded capture queue serves all holes, including those fading away.
// The transparent annulus shows a softly displaced live desktop around lava.
internal sealed class HeatHazeManager
{
    private sealed record Entry(Canvas Owner, Drawing.Rectangle Region, WriteableBitmap Frame, WpfImage View);
    private readonly List<Entry> entries = [];
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(60) };
    private bool busy;
    private int cursor;
    private int generation;

    public HeatHazeManager() => timer.Tick += async (_, _) => await UpdateAsync();

    public void Add(Canvas owner, int screenLeft, int screenTop, int width, int height)
    {
        const int margin = 42;
        var region = new Drawing.Rectangle(screenLeft - margin, screenTop - margin, width + margin * 2, height + margin * 2);
        var frame = new WriteableBitmap(region.Width, region.Height, 96, 96, PixelFormats.Bgr32, null);
        var view = new WpfImage
        {
            Source = frame, Width = region.Width, Height = region.Height, Opacity = 0,
            IsHitTestVisible = false,
            Effect = new BlurEffect { Radius = 7, RenderingBias = RenderingBias.Performance },
            OpacityMask = new RadialGradientBrush(new GradientStopCollection
            {
                new(Colors.Transparent, 0), new(Colors.Transparent, .43),
                new(Colors.White, .61), new(Colors.White, .76), new(Colors.Transparent, 1)
            })
        };
        var drift = new TranslateTransform();
        drift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-3, 3, TimeSpan.FromSeconds(.8))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(2, -2, TimeSpan.FromSeconds(1.1))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        view.RenderTransform = drift;
        Canvas.SetLeft(view, -margin);
        Canvas.SetTop(view, -margin);
        Canvas.SetZIndex(view, -5);
        owner.Children.Insert(0, view);
        entries.Add(new Entry(owner, region, frame, view));
        timer.Start();
    }

    public void Remove(Canvas owner)
    {
        entries.RemoveAll(item => ReferenceEquals(item.Owner, owner));
        if (entries.Count == 0) timer.Stop();
    }

    public void Stop()
    {
        generation++;
        timer.Stop();
        entries.Clear();
        cursor = 0;
    }

    private async Task UpdateAsync()
    {
        if (busy || entries.Count == 0) return;
        busy = true;
        var epoch = generation;
        var batch = new List<Entry>();
        for (var i = 0; i < Math.Min(4, entries.Count); i++)
            batch.Add(entries[(cursor++) % entries.Count]);
        cursor %= entries.Count;
        try
        {
            var results = await Task.Run(() => batch.Select(entry => (Entry: entry, Pixels: Capture(entry.Region))).ToArray());
            if (epoch != generation) return;
            foreach (var (entry, pixels) in results)
            {
                if (!entries.Contains(entry)) continue;
                if (pixels == null) { entry.View.Opacity = 0; continue; }
                entry.Frame.WritePixels(new Int32Rect(0, 0, entry.Region.Width, entry.Region.Height), pixels, entry.Region.Width * 4, 0);
                entry.View.Opacity = .46;
            }
        }
        finally { busy = false; }
    }

    private static byte[]? Capture(Drawing.Rectangle region)
    {
        try
        {
            using var bitmap = new Drawing.Bitmap(region.Width, region.Height, Drawing.Imaging.PixelFormat.Format32bppRgb);
            using var graphics = Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(region.Location, Drawing.Point.Empty, region.Size, Drawing.CopyPixelOperation.SourceCopy);
            var bits = bitmap.LockBits(new Drawing.Rectangle(0, 0, region.Width, region.Height),
                Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                var bytes = new byte[region.Width * region.Height * 4];
                Marshal.Copy(bits.Scan0, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { bitmap.UnlockBits(bits); }
        }
        catch (Exception) { return null; }
    }
}
