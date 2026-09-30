using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace FirawH4K;

public partial class RiftEffectWindow : Window
{
    private double originX;
    private double originY;
    private HwndSource? hwndSource;
    private readonly System.Windows.Threading.DispatcherTimer hideTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    public RiftEffectWindow()
    {
        InitializeComponent();
        SourceInitialized += RiftEffectWindow_SourceInitialized;
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            Hide();
        };
    }

    public void PlayAt(System.Drawing.Rectangle bounds, double screenX, double screenY, bool red)
    {
        var virtualScreen = MonitorRouting.ActiveDesktopBounds();
        originX = virtualScreen.Left;
        originY = virtualScreen.Top;
        Left = virtualScreen.Left;
        Top = virtualScreen.Top;
        Width = virtualScreen.Width;
        Height = virtualScreen.Height;
        RiftCanvas.Children.Clear();

        var accent = red ? MediaColor.FromRgb(255, 66, 82) : MediaColor.FromRgb(66, 245, 179);
        var soft = new SolidColorBrush(MediaColor.FromArgb(215, accent.R, accent.G, accent.B));
        var dark = new SolidColorBrush(MediaColor.FromArgb(90, accent.R, accent.G, accent.B));
        var localX = screenX - originX;
        var localY = screenY - originY;
        var left = Math.Abs(screenX - bounds.Left);
        var right = Math.Abs(bounds.Right - screenX);
        var top = Math.Abs(screenY - bounds.Top);
        var bottom = Math.Abs(bounds.Bottom - screenY);
        var edge = Math.Min(Math.Min(left, right), Math.Min(top, bottom));

        // A sombra irregular cobre os pixels da área de trabalho por um instante,
        // criando a sensação de que a janela foi retirada de trás da imagem.
        var hole = new Polygon
        {
            Fill = new SolidColorBrush(MediaColor.FromArgb(235, 1, 4, 7)),
            Stroke = soft,
            StrokeThickness = 2.4,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accent,
                BlurRadius = 28,
                ShadowDepth = 0,
                Opacity = 0.85
            },
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
        };
        if (edge == left || edge == right)
        {
            hole.Points = new PointCollection
            {
                new(0, 0), new(53, 10), new(38, 38), new(91, 61),
                new(62, 92), new(104, 127), new(54, 156), new(77, 190),
                new(34, 220), new(0, 232)
            };
            var holeX = edge == left ? bounds.Left - originX - 4 : bounds.Right - originX - 104;
            Canvas.SetLeft(hole, holeX);
            Canvas.SetTop(hole, localY - 116);
            if (edge == right)
                hole.RenderTransform = new ScaleTransform(-1, 1, 52, 116);
        }
        else
        {
            hole.Points = new PointCollection
            {
                new(0, 0), new(23, 43), new(47, 29), new(71, 91),
                new(107, 56), new(132, 106), new(163, 58), new(198, 81),
                new(218, 33), new(232, 0)
            };
            Canvas.SetLeft(hole, localX - 116);
            Canvas.SetTop(hole, edge == top ? bounds.Top - originY - 4 : bounds.Bottom - originY - 106);
            if (edge == bottom)
                hole.RenderTransform = new ScaleTransform(1, -1, 116, 53);
        }
        RiftCanvas.Children.Add(hole);

        if (edge == left || edge == right)
        {
            var edgeX = edge == left ? bounds.Left - originX + 3 : bounds.Right - originX - 3;
            var direction = edge == left ? 1 : -1;
            AddLine(edgeX, localY - 92, edgeX + direction * 20, localY - 53, soft, 2.5);
            AddLine(edgeX + direction * 20, localY - 53, edgeX - direction * 11, localY - 14, soft, 2.2);
            AddLine(edgeX - direction * 11, localY - 14, edgeX + direction * 28, localY + 20, soft, 2.8);
            AddLine(edgeX + direction * 28, localY + 20, edgeX + direction * 5, localY + 61, soft, 2.1);
            AddLine(edgeX + direction * 5, localY + 61, edgeX + direction * 34, localY + 101, soft, 2.5);
            AddLine(edgeX, localY - 112, edgeX + direction * 118, localY - 82, dark, 1.2);
            AddLine(edgeX, localY + 112, edgeX + direction * 132, localY + 78, dark, 1.2);
        }
        else
        {
            var edgeY = edge == top ? bounds.Top - originY + 3 : bounds.Bottom - originY - 3;
            var direction = edge == top ? 1 : -1;
            AddLine(localX - 92, edgeY, localX - 53, edgeY + direction * 20, soft, 2.5);
            AddLine(localX - 53, edgeY + direction * 20, localX - 14, edgeY - direction * 11, soft, 2.2);
            AddLine(localX - 14, edgeY - direction * 11, localX + 20, edgeY + direction * 28, soft, 2.8);
            AddLine(localX + 20, edgeY + direction * 28, localX + 61, edgeY + direction * 5, soft, 2.1);
            AddLine(localX + 61, edgeY + direction * 5, localX + 101, edgeY + direction * 34, soft, 2.5);
            AddLine(localX - 112, edgeY, localX - 82, edgeY + direction * 118, dark, 1.2);
            AddLine(localX + 112, edgeY, localX + 78, edgeY + direction * 132, dark, 1.2);
        }

        for (var i = 0; i < 8; i++)
        {
            var particle = new Ellipse
            {
                Width = 3 + (i % 3),
                Height = 3 + (i % 3),
                Fill = soft,
                Opacity = 0.8
            };
            Canvas.SetLeft(particle, localX + Random.Shared.Next(-125, 126));
            Canvas.SetTop(particle, localY + Random.Shared.Next(-120, 121));
            RiftCanvas.Children.Add(particle);
        }

        RiftCanvas.Opacity = 0;
        if (!IsVisible)
            Show();
        RiftCanvas.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            [
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))),
                new LinearDoubleKeyFrame(0.88, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(470))),
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1350)))
            ]
        });
        hideTimer.Stop();
        hideTimer.Interval = TimeSpan.FromMilliseconds(1500);
        hideTimer.Start();
    }

    public void PlayEmergenceAt(double screenX, double screenY, bool red)
    {
        var virtualScreen = MonitorRouting.ActiveDesktopBounds();
        originX = virtualScreen.Left;
        originY = virtualScreen.Top;
        Left = virtualScreen.Left;
        Top = virtualScreen.Top;
        Width = virtualScreen.Width;
        Height = virtualScreen.Height;
        RiftCanvas.Children.Clear();

        var accent = red ? MediaColor.FromRgb(255, 66, 82) : MediaColor.FromRgb(66, 245, 179);
        var edgeBrush = new SolidColorBrush(MediaColor.FromArgb(245, accent.R, accent.G, accent.B));
        var crackBrush = new SolidColorBrush(MediaColor.FromArgb(135, accent.R, accent.G, accent.B));
        var x = screenX - originX;
        var y = screenY - originY;
        var hole = new Polygon
        {
            Points = new PointCollection
            {
                new(92, 0), new(116, 19), new(147, 12), new(155, 49),
                new(185, 70), new(162, 99), new(180, 129), new(146, 143),
                new(132, 180), new(97, 161), new(62, 179), new(49, 144),
                new(15, 128), new(33, 96), new(8, 67), new(42, 48),
                new(50, 16), new(77, 22)
            },
            Fill = new SolidColorBrush(MediaColor.FromArgb(240, 0, 2, 4)),
            Stroke = edgeBrush,
            StrokeThickness = 2.6,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accent,
                BlurRadius = 28,
                ShadowDepth = 0,
                Opacity = 0.95
            }
        };
        Canvas.SetLeft(hole, x - 96);
        Canvas.SetTop(hole, y - 90);
        RiftCanvas.Children.Add(hole);

        AddLine(x - 61, y - 61, x - 122, y - 111, crackBrush, 1.5);
        AddLine(x + 55, y - 62, x + 128, y - 121, crackBrush, 1.5);
        AddLine(x - 82, y + 29, x - 145, y + 54, crackBrush, 1.5);
        AddLine(x + 84, y + 35, x + 147, y + 73, crackBrush, 1.5);
        AddLine(x - 31, y + 77, x - 52, y + 142, crackBrush, 1.5);
        AddLine(x + 34, y + 81, x + 58, y + 151, crackBrush, 1.5);

        RiftCanvas.Opacity = 0;
        if (!IsVisible)
            Show();
        RiftCanvas.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            [
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))),
                new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560))),
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400)))
            ]
        });
        hideTimer.Stop();
        hideTimer.Interval = TimeSpan.FromMilliseconds(1500);
        hideTimer.Start();
    }

    public void PlayExtractionAt(double screenX, double screenY)
    {
        PlayEmergenceAt(screenX, screenY, red: false);
        var x = screenX - originX;
        var y = screenY - originY;
        var accent = MediaColor.FromRgb(63, 225, 134);
        var glint = new SolidColorBrush(MediaColor.FromArgb(185, accent.R, accent.G, accent.B));
        for (var i = 0; i < 12; i++)
        {
            var angle = i * Math.PI * 2 / 12;
            var radius = 105 + (i % 3) * 24;
            var flake = new Polygon
            {
                Points = new PointCollection
                {
                    new(0, 0), new(22 + i % 3 * 6, 7), new(7, 29 + i % 4 * 5)
                },
                Fill = new SolidColorBrush(MediaColor.FromArgb(225, 2, 10, 11)),
                Stroke = glint,
                StrokeThickness = 1.4,
                RenderTransform = new RotateTransform(angle * 180 / Math.PI)
            };
            Canvas.SetLeft(flake, x + Math.Cos(angle) * radius);
            Canvas.SetTop(flake, y + Math.Sin(angle) * radius);
            RiftCanvas.Children.Add(flake);
        }

        if (RiftCanvas.Children.OfType<Polygon>().FirstOrDefault() is Polygon hole)
        {
            var scale = new ScaleTransform(0.28, 0.28, 96, 90);
            hole.RenderTransform = scale;
            var ease = new ElasticEase { Oscillations = 2, Springiness = 4, EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.18, 1.5, TimeSpan.FromMilliseconds(1000)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.18, 1.35, TimeSpan.FromMilliseconds(1000)) { EasingFunction = ease });
        }

        RiftCanvas.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            [
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300))),
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3000)))
            ]
        });
        hideTimer.Stop();
        hideTimer.Interval = TimeSpan.FromMilliseconds(3100);
        hideTimer.Start();
    }

    public void PlayShellSplitAt(double screenX, double screenY)
    {
        var virtualScreen = MonitorRouting.ActiveDesktopBounds();
        originX = virtualScreen.Left;
        originY = virtualScreen.Top;
        Left = virtualScreen.Left;
        Top = virtualScreen.Top;
        Width = virtualScreen.Width;
        Height = virtualScreen.Height;
        RiftCanvas.Children.Clear();
        var x = screenX - originX;
        var y = screenY - originY;
        var crimson = MediaColor.FromRgb(255, 48, 61);
        var green = MediaColor.FromRgb(69, 240, 144);

        var core = new Ellipse
        {
            Width = 112,
            Height = 155,
            Fill = new RadialGradientBrush(MediaColor.FromArgb(225, 112, 5, 14), MediaColor.FromArgb(0, 10, 1, 2)),
            Stroke = new SolidColorBrush(crimson),
            StrokeThickness = 1.5,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = crimson, BlurRadius = 45, ShadowDepth = 0, Opacity = 1 }
        };
        Canvas.SetLeft(core, x - 56);
        Canvas.SetTop(core, y - 78);
        RiftCanvas.Children.Add(core);

        var leftShell = new Polygon
        {
            Points = new PointCollection { new(74, 0), new(39, 10), new(14, 39), new(5, 83), new(22, 132), new(65, 159), new(80, 121), new(66, 99), new(83, 77), new(66, 54), new(79, 30) },
            Fill = new SolidColorBrush(MediaColor.FromArgb(247, 3, 12, 13)),
            Stroke = new SolidColorBrush(green),
            StrokeThickness = 2.2,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = green, BlurRadius = 15, ShadowDepth = 0, Opacity = 0.7 }
        };
        var rightShell = new Polygon
        {
            Points = new PointCollection { new(16, 0), new(51, 10), new(76, 39), new(85, 83), new(68, 132), new(25, 159), new(10, 121), new(24, 99), new(7, 77), new(24, 54), new(11, 30) },
            Fill = new SolidColorBrush(MediaColor.FromArgb(247, 3, 12, 13)),
            Stroke = new SolidColorBrush(green),
            StrokeThickness = 2.2,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = green, BlurRadius = 15, ShadowDepth = 0, Opacity = 0.7 }
        };
        Canvas.SetLeft(leftShell, x - 88);
        Canvas.SetTop(leftShell, y - 80);
        Canvas.SetLeft(rightShell, x - 2);
        Canvas.SetTop(rightShell, y - 80);
        RiftCanvas.Children.Add(leftShell);
        RiftCanvas.Children.Add(rightShell);

        var leftMove = new TranslateTransform();
        var rightMove = new TranslateTransform();
        leftShell.RenderTransform = leftMove;
        rightShell.RenderTransform = rightMove;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        leftMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -75, TimeSpan.FromMilliseconds(850)) { EasingFunction = ease });
        rightMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 75, TimeSpan.FromMilliseconds(850)) { EasingFunction = ease });
        leftShell.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1050)));
        rightShell.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1050)));

        var redBrush = new SolidColorBrush(crimson);
        AddLine(x, y - 74, x - 20, y - 104, redBrush, 2.4);
        AddLine(x, y - 74, x + 23, y - 111, redBrush, 2.4);
        AddLine(x, y + 69, x - 28, y + 113, redBrush, 2.2);
        AddLine(x, y + 69, x + 27, y + 116, redBrush, 2.2);
        RiftCanvas.Opacity = 1;
        if (!IsVisible) Show();
        RiftCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1450)));
        hideTimer.Stop();
        hideTimer.Interval = TimeSpan.FromMilliseconds(1500);
        hideTimer.Start();
    }

    private void AddLine(double x1, double y1, double x2, double y2, MediaBrush brush, double thickness)
    {
        RiftCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = ((SolidColorBrush)brush).Color,
                BlurRadius = 9,
                ShadowDepth = 0,
                Opacity = 0.9
            }
        });
    }

    private void RiftEffectWindow_SourceInitialized(object? sender, EventArgs e)
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
