using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FirawH4K;

public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer followTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double targetLeft;
    private double targetTop;
    private double desiredHeading;
    private double motionStrength;
    private double walkPhase;
    private double animationRate = 1.0;
    private bool pulling;
    private bool fighting;
    private DateTime lastFrameAt = DateTime.UtcNow;
    private readonly RotateTransform leftFrontRotate = new();
    private readonly TranslateTransform leftFrontTranslate = new();
    private readonly RotateTransform leftMiddleRotate = new();
    private readonly TranslateTransform leftMiddleTranslate = new();
    private readonly RotateTransform leftRearRotate = new();
    private readonly TranslateTransform leftRearTranslate = new();
    private readonly RotateTransform rightFrontRotate = new();
    private readonly TranslateTransform rightFrontTranslate = new();
    private readonly RotateTransform rightMiddleRotate = new();
    private readonly TranslateTransform rightMiddleTranslate = new();
    private readonly RotateTransform rightRearRotate = new();
    private readonly TranslateTransform rightRearTranslate = new();
    private bool hasPosition;
    private HwndSource? hwndSource;

    public OverlayWindow()
    {
        InitializeComponent();
        ConfigureLeg(LeftFrontLeg, leftFrontRotate, leftFrontTranslate, 45, 75);
        ConfigureLeg(LeftMiddleLeg, leftMiddleRotate, leftMiddleTranslate, 43, 125);
        ConfigureLeg(LeftRearLeg, leftRearRotate, leftRearTranslate, 50, 175);
        ConfigureLeg(RightFrontLeg, rightFrontRotate, rightFrontTranslate, 175, 75);
        ConfigureLeg(RightMiddleLeg, rightMiddleRotate, rightMiddleTranslate, 177, 125);
        ConfigureLeg(RightRearLeg, rightRearRotate, rightRearTranslate, 170, 175);
        followTimer.Tick += FollowTimer_Tick;
        IsVisibleChanged += OverlayWindow_IsVisibleChanged;
        SourceInitialized += OverlayWindow_SourceInitialized;
    }

    public void MoveToPoint(int x, int y, double heading, double speed)
    {
        desiredHeading = heading;
        motionStrength = speed;
        targetLeft = x - 125;
        targetTop = y - 125;

        if (!hasPosition)
        {
            Left = targetLeft;
            Top = targetTop;
            hasPosition = true;
        }
    }

    public void SetAnimationSpeed(double speed)
    {
        animationRate = Math.Clamp(speed, 0.5, 10.0) / 3.0;
    }

    public void SetPulling(bool value) => pulling = value;
    public void SetFighting(bool value) => fighting = value;

    public void RaiseAbovePanels()
    {
        if (!IsVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(handle, new nint(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public void SetDefaultSkin() => SetSkin("Assets/cyber-beetle-h4k-dark.png");

    public void SetInfiltratorSkin() => SetSkin("Assets/cyber-beetle-red-dark.png");

    public void PlayInfiltratorBirthAnimation()
    {
        if (AvatarCanvas.RenderTransform is not TransformGroup group) return;
        var scale = (ScaleTransform)group.Children[0];
        var translate = (TranslateTransform)group.Children[1];
        var ease = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut };
        AvatarCanvas.Opacity = 0;
        scale.ScaleX = 0.16;
        scale.ScaleY = 0.16;
        translate.X = -20;
        translate.Y = 26;
        AvatarCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(580)));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.16, 1, TimeSpan.FromMilliseconds(850)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.16, 1, TimeSpan.FromMilliseconds(850)) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-20, 0, TimeSpan.FromMilliseconds(760)));
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(760)));
    }

    private void SetSkin(string assetPath)
    {
        var source = new BitmapImage(new Uri($"pack://application:,,,/{assetPath}"));
        CursorMascot.Source = source;
        LeftFrontImage.Source = source;
        LeftMiddleImage.Source = source;
        LeftRearImage.Source = source;
        RightFrontImage.Source = source;
        RightMiddleImage.Source = source;
        RightRearImage.Source = source;
    }

    public void PlayActivationAnimation()
    {
        AvatarCanvas.Opacity = 0;
        if (AvatarCanvas.RenderTransform is not TransformGroup group)
            return;

        var scale = (ScaleTransform)group.Children[0];
        var translate = (TranslateTransform)group.Children[1];
        scale.ScaleX = 0.82;
        scale.ScaleY = 0.82;
        translate.X = -28;
        translate.Y = 44;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        AvatarCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(620)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(620)) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-28, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(44, 0, TimeSpan.FromMilliseconds(620)) { EasingFunction = ease });

        RiftCanvas.Opacity = 0;
        RiftCanvas.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            [
                new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))),
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(980)))
            ]
        });
        var riftScale = new ScaleTransform(0.55, 0.55, 105, 52);
        RiftCanvas.RenderTransform = riftScale;
        riftScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.55, 1.08, TimeSpan.FromMilliseconds(760)) { EasingFunction = ease });
        riftScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(760)) { EasingFunction = ease });

        ActivationRing.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 0, TimeSpan.FromMilliseconds(760)) { EasingFunction = ease });
        ActivationRing.RenderTransform = new ScaleTransform(0.55, 0.55);
        if (ActivationRing.RenderTransform is ScaleTransform ringScale)
        {
            ringScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.55, 1.22, TimeSpan.FromMilliseconds(760)) { EasingFunction = ease });
            ringScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.55, 1.22, TimeSpan.FromMilliseconds(760)) { EasingFunction = ease });
        }
    }

    private void OverlayWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            lastFrameAt = DateTime.UtcNow;
            followTimer.Start();
        }
        else followTimer.Stop();
    }

    private void FollowTimer_Tick(object? sender, EventArgs e)
    {
        if (!hasPosition) return;

        var now = DateTime.UtcNow;
        var frameSeconds = Math.Clamp((now - lastFrameAt).TotalSeconds, 0.001, 0.20);
        lastFrameAt = now;

        // O overlay acompanha a posição cinemática sem o atraso que dava aparência
        // de estar sendo puxado. O balanço das pernas fornece a suavidade visual.
        var follow = 1 - Math.Exp(-38 * frameSeconds);
        Left += (targetLeft - Left) * follow;
        Top += (targetTop - Top) * follow;

        if (BeetleRig.RenderTransform is not TransformGroup group) return;
        var bodyScale = (ScaleTransform)group.Children[0];
        var bodyRotate = (RotateTransform)group.Children[1];
        var bodyTranslate = (TranslateTransform)group.Children[2];
        var bodySkew = (SkewTransform)group.Children[3];

        var moving = motionStrength > 0.15;
        // A passada é baseada em tempo real, não na quantidade de frames:
        // se o Windows atrasar um frame, o besouro não fica em câmera lenta.
        walkPhase += fighting
            ? 31 * frameSeconds
            : moving || pulling
            ? (15.5 + animationRate * 5.5) * frameSeconds
            : 1.15 * frameSeconds;
        var step = Math.Sin(walkPhase);
        var stride = Math.Cos(walkPhase);
        bodyScale.ScaleX = fighting ? .94 + Math.Sign(step) * .045 : 0.9 + Math.Abs(step) * 0.05;
        bodyScale.ScaleY = fighting ? .94 - Math.Sign(step) * .04 : 0.9 + Math.Abs(stride) * 0.05;
        var desiredBodyAngle = desiredHeading * 180 / Math.PI + 90;
        var angleError = NormalizeDegrees(desiredBodyAngle - bodyRotate.Angle);
        bodyRotate.Angle += angleError * 0.34;
        bodyTranslate.X = fighting ? (step >= 0 ? 4 : -3) : pulling ? stride * 5.5 : stride * 2.2;
        bodyTranslate.Y = fighting ? (step >= 0 ? -5 : 2) : pulling ? -Math.Abs(step) * 12.5 : moving ? -Math.Abs(step) * 8.5 : -Math.Abs(step) * 1.5;
        bodySkew.AngleX += ((fighting ? step * 5 : moving ? Math.Sin(desiredHeading) * 3.5 : 0) - bodySkew.AngleX) * 0.25;
        bodySkew.AngleY += ((fighting ? stride * 3 : moving ? step * 1.2 : step * 0.25) - bodySkew.AngleY) * 0.22;

        AnimateLegs(step, stride);
    }

    private static double NormalizeDegrees(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    private static void ConfigureLeg(Canvas leg, RotateTransform rotate, TranslateTransform translate, double centerX, double centerY)
    {
        rotate.CenterX = centerX;
        rotate.CenterY = centerY;
        leg.RenderTransform = new TransformGroup
        {
            Children = { rotate, translate }
        };
    }

    private void AnimateLegs(double step, double stride)
    {
        AnimateLeg(leftFrontRotate, leftFrontTranslate, step, stride, 45, 75, 13.0, 6.3);
        AnimateLeg(leftMiddleRotate, leftMiddleTranslate, -step, -stride, 43, 125, 15.0, 7.0);
        AnimateLeg(leftRearRotate, leftRearTranslate, step, stride, 50, 175, 14.0, 6.5);
        AnimateLeg(rightFrontRotate, rightFrontTranslate, -step, -stride, 175, 75, 13.0, 6.3);
        AnimateLeg(rightMiddleRotate, rightMiddleTranslate, step, stride, 177, 125, 15.0, 7.0);
        AnimateLeg(rightRearRotate, rightRearTranslate, -step, -stride, 170, 175, 14.0, 6.5);
    }

    private static void AnimateLeg(
        RotateTransform rotate,
        TranslateTransform translate,
        double step,
        double stride,
        double centerX,
        double centerY,
        double rotation,
        double lift)
    {
        rotate.CenterX = centerX;
        rotate.CenterY = centerY;
        rotate.Angle = step * rotation;
        translate.X = stride * 8.0;
        translate.Y = -Math.Abs(step) * lift;
    }

    private void OverlayWindow_SourceInitialized(object? sender, EventArgs e)
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int width, int height, uint flags);
}
