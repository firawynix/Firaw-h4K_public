using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace FirawH4K;

public partial class MatrixTrailWindow : Window
{
    private sealed class Glyph
    {
        public required TextBlock View { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Vy { get; init; }
        public double Age { get; set; }
        public double Life { get; init; }
    }

    private sealed class MatrixColumn
    {
        public required TextBlock[] Views { get; init; }
        public double X { get; set; }
        public double HeadY { get; set; }
        public double Speed { get; init; }
    }

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(95) };
    private readonly List<Glyph> glyphs = [];
    private readonly List<MatrixColumn> columns = [];
    private readonly Random random = new();
    private static readonly string GlyphSet = "アイウエオカキクケコ01<>/{}[]$#&*+=!?;:~^%@ABCDEF";
    private HwndSource? hwndSource;
    private double originX;
    private double originY;
    private bool started;
    private DateTime nextTrailAt;
    private DateTime lastTickAt;
    private MediaColor accent = MediaColor.FromRgb(66, 245, 179);

    public MatrixTrailWindow()
    {
        InitializeComponent();
        timer.Tick += Timer_Tick;
        SourceInitialized += MatrixTrailWindow_SourceInitialized;
    }

    public void StartTrail() => StartTrail(MediaColor.FromRgb(66, 245, 179));

    public void StartTrail(MediaColor accentColor)
    {
        accent = accentColor;
        var screen = MonitorRouting.ActiveDesktopBounds();
        originX = screen.Left;
        originY = screen.Top;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;
        TrailCanvas.Children.Clear();
        glyphs.Clear();
        columns.Clear();
        CreateMatrixColumns();
        lastTickAt = DateTime.UtcNow;
        if (!IsVisible) Show();
        started = true;
        timer.Start();
    }

    public void AddTrailPoint(double screenX, double screenY)
    {
        if (!started) return;
        var x = screenX - originX;
        var y = screenY - originY;
        var now = DateTime.UtcNow;
        if (now < nextTrailAt)
            return;
        nextTrailAt = now.AddMilliseconds(100);

        var count = 3 + random.Next(3);
        for (var i = 0; i < count; i++)
        {
            var localX = x + random.NextDouble() * 110 - 55;
            var localY = y + random.NextDouble() * 72 - 36;
            var view = new TextBlock
            {
                Text = GlyphSet[random.Next(GlyphSet.Length)].ToString(),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 21 + random.Next(10),
                FontWeight = FontWeights.Bold,
                Foreground = CreateGlyphBrush(235),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(view, localX);
            Canvas.SetTop(view, localY);
            TrailCanvas.Children.Add(view);
            glyphs.Add(new Glyph { View = view, X = localX, Y = localY, Vy = 24 + random.NextDouble() * 54, Life = 950 + random.NextDouble() * 1000 });
        }
        TrimGlyphs();
    }

    public void StopTrail()
    {
        started = false;
        timer.Stop();
        glyphs.Clear();
        columns.Clear();
        TrailCanvas.Children.Clear();
        Hide();
    }

    private void CreateMatrixColumns()
    {
        // Keep the rain broad and visible without creating hundreds of independent
        // WPF text layouts on large multi-monitor desktops.
        var count = Math.Clamp((int)(Width / 54), 26, 38);
        for (var columnIndex = 0; columnIndex < count; columnIndex++)
        {
            var length = 7 + random.Next(4);
            var views = new TextBlock[length];
            var x = random.NextDouble() * Math.Max(1, Width - 20);
            var headY = -random.NextDouble() * Math.Max(80, Height);
            for (var glyphIndex = 0; glyphIndex < length; glyphIndex++)
            {
                var view = new TextBlock
                {
                    Text = GlyphSet[random.Next(GlyphSet.Length)].ToString(),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 15 + random.Next(8),
                    FontWeight = FontWeights.Bold,
                    Foreground = CreateRainBrush(220),
                    IsHitTestVisible = false,
                };
                TrailCanvas.Children.Add(view);
                views[glyphIndex] = view;
            }
            columns.Add(new MatrixColumn
            {
                Views = views,
                X = x,
                HeadY = headY,
                Speed = 145 + random.NextDouble() * 255,
            });
        }
    }

    private SolidColorBrush CreateGlyphBrush(byte alpha)
    {
        return new SolidColorBrush(MediaColor.FromArgb(
            alpha,
            (byte)Math.Clamp(accent.R + random.Next(-18, 18), 0, 255),
            (byte)Math.Clamp(accent.G + random.Next(-18, 18), 0, 255),
            (byte)Math.Clamp(accent.B + random.Next(-18, 18), 0, 255)));
    }

    private SolidColorBrush CreateRainBrush(byte alpha)
    {
        return new SolidColorBrush(MediaColor.FromArgb(
            alpha,
            (byte)Math.Clamp(accent.R + random.Next(-24, 24), 0, 255),
            (byte)Math.Clamp(accent.G + random.Next(-24, 24), 0, 255),
            (byte)Math.Clamp(accent.B + random.Next(-24, 24), 0, 255)));
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var seconds = Math.Clamp((now - lastTickAt).TotalSeconds, 0.001, 0.12);
        lastTickAt = now;
        foreach (var column in columns)
        {
            column.HeadY += column.Speed * seconds;
            if (column.HeadY - column.Views.Length * 17 > Height)
            {
                column.HeadY = -random.Next(40, Math.Max(100, (int)Height / 2));
                column.X = random.NextDouble() * Math.Max(1, Width - 20);
            }

            for (var index = 0; index < column.Views.Length; index++)
            {
                var view = column.Views[index];
                Canvas.SetLeft(view, column.X);
                Canvas.SetTop(view, column.HeadY - index * 17);
                view.Opacity = Math.Max(0, 0.9 - index * 0.085);
                if (random.NextDouble() < 0.12)
                    view.Text = GlyphSet[random.Next(GlyphSet.Length)].ToString();
            }
        }

        var expired = new List<Glyph>();
        foreach (var glyph in glyphs)
        {
            glyph.Age += seconds * 1000;
            glyph.Y += glyph.Vy * seconds;
            var progress = Math.Clamp(glyph.Age / glyph.Life, 0, 1);
            glyph.View.Opacity = 0.84 * (1 - progress);
            Canvas.SetLeft(glyph.View, glyph.X);
            Canvas.SetTop(glyph.View, glyph.Y);
            if (progress >= 1) expired.Add(glyph);
        }
        foreach (var glyph in expired)
        {
            glyphs.Remove(glyph);
            TrailCanvas.Children.Remove(glyph.View);
        }
    }

    private void TrimGlyphs()
    {
        while (glyphs.Count > 70)
        {
            var glyph = glyphs[0];
            glyphs.RemoveAt(0);
            TrailCanvas.Children.Remove(glyph.View);
        }
    }

    private void MatrixTrailWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        hwndSource = HwndSource.FromHwnd(handle);
        hwndSource.AddHook(WindowProc);
        var styles = GetWindowLong(handle, -20);
        const nint WsExTransparent = 0x00000020;
        const nint WsExToolWindow = 0x00000080;
        const nint WsExNoActivate = 0x08000000;
        SetWindowLong(handle, -20, styles | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0084)
        {
            handled = true;
            return new IntPtr(-1);
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern nint GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLong(nint hWnd, int nIndex, nint nIndexValue);
}
