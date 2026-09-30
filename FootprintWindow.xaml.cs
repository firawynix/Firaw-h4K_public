using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace FirawH4K;

public partial class FootprintWindow : Window
{
    private sealed class Mark
    {
        public required FrameworkElement View { get; init; }
        public DateTime Created { get; init; }
        public double LifeMs { get; init; }
    }

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly List<Mark> marks = [];
    private double originX;
    private double originY;
    private double lastGreenX;
    private double lastGreenY;
    private double lastRedX;
    private double lastRedY;
    private bool hasGreenPoint;
    private bool hasRedPoint;
    private bool started;
    private HwndSource? hwndSource;

    public FootprintWindow()
    {
        InitializeComponent();
        timer.Tick += Timer_Tick;
        SourceInitialized += FootprintWindow_SourceInitialized;
    }

    public void StartTrail()
    {
        var screen = MonitorRouting.ActiveDesktopBounds();
        originX = screen.Left;
        originY = screen.Top;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;
        FootprintCanvas.Children.Clear();
        marks.Clear();
        hasGreenPoint = false;
        hasRedPoint = false;
        started = true;
        if (!IsVisible)
            Show();
        timer.Start();
    }

    public void AddFootprints(double screenX, double screenY, double heading, bool red)
    {
        if (!started)
            return;

        ref var lastX = ref red ? ref lastRedX : ref lastGreenX;
        ref var lastY = ref red ? ref lastRedY : ref lastGreenY;
        ref var hasPoint = ref red ? ref hasRedPoint : ref hasGreenPoint;
        if (hasPoint && Math.Sqrt(Math.Pow(screenX - lastX, 2) + Math.Pow(screenY - lastY, 2)) < 82)
            return;

        lastX = screenX;
        lastY = screenY;
        hasPoint = true;

        var accent = red
            ? MediaColor.FromArgb(185, 255, 66, 82)
            : MediaColor.FromArgb(185, 66, 245, 179);
        var brush = new SolidColorBrush(accent);
        var normalX = -Math.Sin(heading);
        var normalY = Math.Cos(heading);
        var now = DateTime.UtcNow;
        AddMark(screenX + normalX * 14, screenY + normalY * 14, heading, brush, now);
        AddMark(screenX - normalX * 14, screenY - normalY * 14, heading, brush, now);
        while (marks.Count > 120)
        {
            FootprintCanvas.Children.Remove(marks[0].View);
            marks.RemoveAt(0);
        }
    }

    public void StopTrail()
    {
        started = false;
        timer.Stop();
        marks.Clear();
        FootprintCanvas.Children.Clear();
        Hide();
    }

    private void AddMark(double screenX, double screenY, double heading, MediaBrush brush, DateTime now)
    {
        var mark = new Canvas
        {
            Width = 46,
            Height = 46,
            Opacity = 0.9,
            IsHitTestVisible = false,
            RenderTransform = new RotateTransform(heading * 180 / Math.PI + 90, 23, 23)
        };
        mark.Children.Add(new Polygon
        {
            Points = new PointCollection { new(23, 12), new(32, 17), new(30, 29), new(23, 34), new(14, 29), new(13, 18) },
            Fill = new SolidColorBrush(MediaColor.FromArgb(38, ((SolidColorBrush)brush).Color.R, ((SolidColorBrush)brush).Color.G, ((SolidColorBrush)brush).Color.B)),
            Stroke = brush,
            StrokeThickness = 1.6
        });
        mark.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 23,23 L 23,12 L 20,8 L 19,1 M 25,22 L 32,17 L 39,16 L 44,12 M 25,24 L 30,29 L 35,32 L 38,42 M 22,25 L 23,34 L 19,39 L 18,45 M 21,23 L 14,29 L 9,30 L 2,34 M 21,21 L 13,18 L 9,13 L 2,11 M 23,12 L 28,8 L 31,3 M 30,29 L 36,26 L 44,27 M 14,29 L 10,36 L 7,42"),
            Stroke = brush,
            StrokeThickness = 1.1,
            Opacity = 0.8
        });
        var dot = new Ellipse { Width = 6, Height = 6, Fill = brush };
        Canvas.SetLeft(dot, 20);
        Canvas.SetTop(dot, 20);
        mark.Children.Add(dot);
        Canvas.SetLeft(mark, screenX - originX - 23);
        Canvas.SetTop(mark, screenY - originY - 23);
        FootprintCanvas.Children.Add(mark);
        marks.Add(new Mark { View = mark, Created = now, LifeMs = 10000 + Random.Shared.Next(4500) });
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        foreach (var mark in marks.ToArray())
        {
            var progress = (now - mark.Created).TotalMilliseconds / mark.LifeMs;
            if (progress >= 1)
            {
                marks.Remove(mark);
                FootprintCanvas.Children.Remove(mark.View);
                continue;
            }

            mark.View.Opacity = 0.9 * (1 - progress);
        }
    }

    private void FootprintWindow_SourceInitialized(object? sender, EventArgs e)
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
