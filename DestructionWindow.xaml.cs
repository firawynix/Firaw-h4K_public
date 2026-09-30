using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace FirawH4K;

public partial class DestructionWindow : Window
{
    private const int MaxFractures = 24;
    private static readonly SolidColorBrush RedBright = new(MediaColor.FromRgb(179, 16, 43));
    private static readonly SolidColorBrush RedDim = new(MediaColor.FromRgb(51, 7, 19));
    private static readonly SolidColorBrush GreenBright = new(MediaColor.FromRgb(42, 135, 84));
    private static readonly SolidColorBrush GreenDim = new(MediaColor.FromRgb(7, 48, 32));
    private static readonly BitmapImage AbyssBorder = LoadArt("abyss-border-v4.png");
    private static readonly BitmapImage AbyssPlaque = LoadArt("abyss-plaque-v2.png");
    private static readonly BitmapImage AbyssImpact = LoadArt("abyss-impact-v3.png");
    private static readonly Dictionary<BitmapSource, (ImageBrush Red, ImageBrush Green)> EmissionMasks = [];
    private readonly DispatcherTimer pulse = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly Queue<Canvas> fractures = [];
    private readonly HeatHazeManager heatHaze = new();
    private readonly List<(Shape Shape, bool Red)> roots = [];
    private readonly List<(TextBlock Title, Polygon Frame)> titles = [];
    private readonly List<Ellipse> eyes = [];
    private readonly List<(Image Frame, TextBlock Title, System.Windows.Media.Effects.DropShadowEffect Glow)> abyssTitles = [];
    private readonly List<(Ellipse Glow, bool Red, double Phase)> abyssGlows = [];
    private readonly List<(Ellipse Glow, bool Red, double Phase)> impactGlows = [];
    private readonly Dictionary<Drawing.Rectangle, BitmapSource> snapshots = [];
    private readonly Random random = new();
    private Drawing.Rectangle virtualScreen;
    private int frame;
    private int impactsSinceHealing;

    public DestructionWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        pulse.Tick += (_, _) => Pulse();
    }

    public void StartEffect(bool showFrame = true)
    {
        StopEffect();
        virtualScreen = MonitorRouting.ActiveDesktopBounds();
        Left = virtualScreen.Left;
        Top = virtualScreen.Top;
        Width = virtualScreen.Width;
        Height = virtualScreen.Height;
        Scene.Width = Width;
        Scene.Height = Height;

        // Capture the visible monitors only to memory, before this overlay appears.
        foreach (var screen in MonitorRouting.ActiveDesktopScreens())
        {
            if (!showFrame)
                CaptureScreen(screen);
            if (showFrame)
            {
                DrawAbyssFrame(screen);
                DrawAbyssTitle(screen);
            }
        }
        if (!IsVisible) Show();
        pulse.Start();
        Pulse();
    }

    public void RefreshSnapshots(Drawing.Rectangle fightScreen)
    {
        if (!IsVisible) return;
        // The fictional windows are placed after the first capture. Refreshing
        // before a fight includes them in the fractured image without clearing
        // the cracks already left on this overlay.
        Hide();
        try
        {
            CaptureScreen(fightScreen);
        }
        finally { Show(); }
    }

    private void CaptureScreen(Drawing.Rectangle screen)
    {
        try
        {
            using var bitmap = new Drawing.Bitmap(screen.Width, screen.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, bitmap.Size, Drawing.CopyPixelOperation.SourceCopy);
            var hbitmap = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                snapshots[screen] = source;
            }
            finally { DeleteObject(hbitmap); }
        }
        catch (Exception)
        {
            // Protected/exclusive surfaces can reject capture; cracks still render.
        }
    }

    public void AddImpact(double screenX, double screenY, Drawing.Rectangle screen, bool liveShards = false)
    {
        if (!IsVisible || !screen.Contains((int)screenX, (int)screenY)) return;
        var centerX = Math.Clamp(screenX + random.Next(-12, 13), screen.Left + 75, screen.Right - 75);
        var centerY = Math.Clamp(screenY + random.Next(-12, 13), screen.Top + 75, screen.Bottom - 75);
        const int half = 132;
        var left = (int)Math.Max(screen.Left, centerX - half);
        var top = (int)Math.Max(screen.Top, centerY - half);
        var right = (int)Math.Min(screen.Right, centerX + half);
        var bottom = (int)Math.Min(screen.Bottom, centerY + half);
        var width = right - left;
        var height = bottom - top;
        if (width < 90 || height < 90) return;

        BitmapSource? crop = null;
        if (snapshots.TryGetValue(screen, out var snapshot))
        {
            // Make a compact independent tile. A CroppedBitmap can retain the
            // full monitor surface for every fracture and exhaust memory.
            var stride = (width * snapshot.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * height];
            snapshot.CopyPixels(new Int32Rect(left - screen.Left, top - screen.Top, width, height), pixels, stride, 0);
            crop = BitmapSource.Create(width, height, 96, 96, snapshot.Format, snapshot.Palette, pixels, stride);
            crop.Freeze();
        }
        var fracture = CreateFracture(width, height, crop);
        if (liveShards)
        {
            var localImage = CaptureTile(left, top, width, height);
            if (localImage != null)
                AddTemporaryShards(fracture, width, height, localImage);
        }
        Canvas.SetLeft(fracture, left - virtualScreen.Left);
        Canvas.SetTop(fracture, top - virtualScreen.Top);
        heatHaze.Add(fracture, left, top, width, height);
        Scene.Children.Add(fracture);
        fractures.Enqueue(fracture);
        fracture.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(145)));
        if (++impactsSinceHealing == 3)
        {
            impactsSinceHealing = 0;
            if (fractures.Count > 3) HealOldest();
        }
        while (fractures.Count > MaxFractures)
            RemoveFracture(fractures.Dequeue());
    }

    private static BitmapSource? CaptureTile(int left, int top, int width, int height)
    {
        try
        {
            using var bitmap = new Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(left, top, 0, 0, bitmap.Size, Drawing.CopyPixelOperation.SourceCopy);
            var hbitmap = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally { DeleteObject(hbitmap); }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void AddTemporaryShards(Canvas fracture, int width, int height, BitmapSource source)
    {
        var brush = new ImageBrush(source)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, width, height),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, width, height),
            Stretch = Stretch.Fill
        };
        brush.Freeze();
        var cx = width / 2.0;
        var cy = height / 2.0;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            const int pieces = 12;
            for (var i = 0; i < pieces; i++)
            {
                var a = i * 2 * Math.PI / pieces;
                var b = (i + 1) * 2 * Math.PI / pieces;
                var first = new Point(cx + Math.Cos(a) * 123, cy + Math.Sin(a) * 123);
                var second = new Point(cx + Math.Cos(b) * 123, cy + Math.Sin(b) * 123);
                var shift = 5 + i % 3 * 3;
                var mid = (a + b) / 2;
                context.PushTransform(new TranslateTransform(Math.Cos(mid) * shift, Math.Sin(mid) * shift));
                context.DrawGeometry(brush, null, Triangle(new Point(cx, cy), first, second));
                context.Pop();
            }
        }
        var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        frame.Render(visual);
        frame.Freeze();
        var image = new Image
        {
            Source = frame, Width = width, Height = height, Opacity = .74,
            IsHitTestVisible = false
        };
        fracture.Children.Insert(0, image);
        var fade = new DoubleAnimation(.74, 0, TimeSpan.FromMilliseconds(950));
        fade.Completed += (_, _) => fracture.Children.Remove(image);
        image.BeginAnimation(OpacityProperty, fade);
    }

    private Canvas CreateFracture(int width, int height, BitmapSource? crop)
    {
        var patch = new Canvas { Width = width, Height = height, IsHitTestVisible = false };
        var cx = width / 2.0;
        var cy = height / 2.0;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawEllipse(new RadialGradientBrush(new GradientStopCollection
            {
                new(MediaColor.FromArgb(76, 0, 1, 3), 0),
                new(MediaColor.FromArgb(28, 1, 5, 8), 0.42),
                new(MediaColor.FromArgb(0, 1, 5, 8), 1)
            }), null, new Point(cx, cy), cx, cy);

            ImageBrush? imageBrush = null;
            if (crop != null)
            {
                imageBrush = new ImageBrush(crop)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, width, height),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, width, height),
                    Stretch = Stretch.Fill
                };
                imageBrush.Freeze();
            }

            const int shardCount = 13;
            var angles = new double[shardCount + 1];
            for (var i = 0; i <= shardCount; i++)
                angles[i] = -Math.PI + i * 2 * Math.PI / shardCount + (i is 0 or shardCount ? 0 : (random.NextDouble() - .5) * .18);
            for (var i = 0; i < shardCount; i++)
            {
                var a = angles[i];
                var b = angles[i + 1];
                var p1 = new Point(cx + Math.Cos(a) * random.Next(92, 130), cy + Math.Sin(a) * random.Next(92, 130));
                var p2 = new Point(cx + Math.Cos(b) * random.Next(92, 130), cy + Math.Sin(b) * random.Next(92, 130));
                var center = new Point(cx + random.Next(-12, 13), cy + random.Next(-12, 13));
                var distance = random.Next(2, 7);
                var direction = (a + b) * .5;
                if (imageBrush != null)
                {
                    context.PushTransform(new TranslateTransform(Math.Cos(direction) * (distance + 7), Math.Sin(direction) * (distance + 7)));
                    context.DrawGeometry(new SolidColorBrush(MediaColor.FromArgb(224, 0, 0, 2)), null, Triangle(center, p1, p2));
                    context.Pop();
                    context.PushTransform(new TranslateTransform(Math.Cos(direction) * distance, Math.Sin(direction) * distance));
                    context.DrawGeometry(imageBrush, null, Triangle(center, p1, p2));
                    context.Pop();
                }
                else
                {
                    // The destructive frame does not retain a desktop photo.
                    // Translucent displaced shards let moving wallpaper show
                    // through while leaving the normal mode's crack geometry.
                    context.PushTransform(new TranslateTransform(Math.Cos(direction) * (distance + 5), Math.Sin(direction) * (distance + 5)));
                    context.DrawGeometry(new SolidColorBrush(MediaColor.FromArgb(92, 0, 0, 3)), null, Triangle(center, p1, p2));
                    context.Pop();
                    context.DrawGeometry(new SolidColorBrush(MediaColor.FromArgb(35, 26, 37, 43)), null, Triangle(center, p1, p2));
                }
                DrawCrack(context, center, p1, i % 2 == 0);
                var branch = new Point(center.X + (p1.X - center.X) * .64, center.Y + (p1.Y - center.Y) * .64);
                DrawCrack(context, branch, new Point(branch.X + Math.Cos(direction + 1.1) * random.Next(15, 45), branch.Y + Math.Sin(direction + 1.1) * random.Next(15, 45)), i % 3 == 0);
                if (i % 3 == 0)
                {
                    var splinter = new Point(cx + Math.Cos(direction) * random.Next(74, 110), cy + Math.Sin(direction) * random.Next(74, 110));
                    var tip = new Point(splinter.X + Math.Cos(direction + .35) * random.Next(10, 24), splinter.Y + Math.Sin(direction + .35) * random.Next(10, 24));
                    DrawCrack(context, splinter, tip, i % 2 != 0);
                }
            }

            // Keep the center translucent so the live desktop and Matrix rain
            // remain visible through the break instead of freezing under it.
        }
        var texture = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        texture.Render(visual);
        texture.Freeze();
        var image = new Image
        {
            Source = texture, Width = width, Height = height, IsHitTestVisible = false,
            RenderTransformOrigin = new Point(.5, .5)
        };
        var scale = new ScaleTransform(.88, .88);
        image.RenderTransform = scale;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.88, 1, TimeSpan.FromMilliseconds(780)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.88, 1, TimeSpan.FromMilliseconds(780)) { EasingFunction = ease });
        patch.Children.Add(image);
        var lava = new LavaSurface(width, height);
        lava.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(430)));
        patch.Children.Add(lava);
        var rim = new Image
        {
            Source = AbyssImpact, Width = width, Height = height,
            Stretch = Stretch.Fill, Opacity = .94, IsHitTestVisible = false,
            RenderTransformOrigin = new Point(.5, .5)
        };
        rim.RenderTransform = new ScaleTransform(.64, .64);
        var rimScale = (ScaleTransform)rim.RenderTransform;
        rimScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.64, 1, TimeSpan.FromMilliseconds(720)) { EasingFunction = ease });
        rimScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.64, 1, TimeSpan.FromMilliseconds(720)) { EasingFunction = ease });
        patch.Children.Add(rim);
        AddMaterialPulse(patch, rim, random.NextDouble() * 3);
        AddGlow(patch, cx - 84, cy - 31, 42, true, impactGlows, .2);
        AddGlow(patch, cx + 49, cy + 22, 42, false, impactGlows, 1.8);
        return patch;
    }

    private static Geometry Triangle(Point center, Point first, Point second)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(center, true, true);
            context.LineTo(first, true, false);
            context.LineTo(second, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void DrawCrack(DrawingContext context, Point from, Point to, bool red)
    {
        context.DrawLine(new System.Windows.Media.Pen(Brushes.Black, 6.3),
            new Point(from.X + 1, from.Y + 1), new Point(to.X + 1, to.Y + 1));
        context.DrawLine(new System.Windows.Media.Pen(
            new SolidColorBrush(MediaColor.FromArgb(124, 139, 142, 139)), .85),
            new Point(from.X - 1.5, from.Y - 1.5), new Point(to.X - 1.5, to.Y - 1.5));
        context.DrawLine(new System.Windows.Media.Pen(
            new SolidColorBrush(MediaColor.FromArgb(180, 42, 46, 49)),
            random.NextDouble() < .3 ? 1.55 : .9), from, to);
    }

    private void HealOldest()
    {
        if (fractures.Count == 0) return;
        var old = fractures.Dequeue();
        old.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(2300)));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2350) };
        timer.Tick += (_, _) => { timer.Stop(); RemoveFracture(old); };
        timer.Start();
    }

    private void RemoveFracture(Canvas fracture)
    {
        heatHaze.Remove(fracture);
        Scene.Children.Remove(fracture);
        impactGlows.RemoveAll(item => ReferenceEquals(item.Glow.Parent, fracture));
    }

    private static BitmapImage LoadArt(string name)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/{typeof(DestructionWindow).Assembly.GetName().Name};component/Assets/{name}");
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void DrawAbyssFrame(Drawing.Rectangle screen)
    {
        var left = screen.Left - virtualScreen.Left;
        var top = screen.Top - virtualScreen.Top;
        var frameImage = new Image
        {
            Source = AbyssBorder, Width = screen.Width, Height = screen.Height,
            Stretch = Stretch.Fill, IsHitTestVisible = false
        };
        Canvas.SetLeft(frameImage, left);
        Canvas.SetTop(frameImage, top);
        Canvas.SetZIndex(frameImage, 890);
        Scene.Children.Add(frameImage);
        AddMaterialPulse(Scene, frameImage, .7);
        AddGlow(Scene, left + screen.Width * .03, top + screen.Height * .17, Math.Min(105, screen.Width * .11), true, abyssGlows, .1, 900);
        AddGlow(Scene, left + screen.Width * .83, top + screen.Height * .72, Math.Min(118, screen.Width * .11), false, abyssGlows, 2.1, 900);
        AddGlow(Scene, left + screen.Width * .27, top + screen.Height * .90, Math.Min(100, screen.Width * .10), true, abyssGlows, 3.4, 900);
        AddGlow(Scene, left + screen.Width * .69, top + screen.Height * .025, Math.Min(108, screen.Width * .10), false, abyssGlows, 5.0, 900);
        // Tiny light pockets follow the darker rockwork and the continuous
        // bottom thorn band. Their phases differ so the border feels alive.
        AddGlow(Scene, left + screen.Width * .015, top + screen.Height * .42, 55, false, abyssGlows, 2.8, 900);
        AddGlow(Scene, left + screen.Width * .022, top + screen.Height * .68, 62, true, abyssGlows, 4.9, 900);
        AddGlow(Scene, left + screen.Width * .945, top + screen.Height * .21, 58, false, abyssGlows, 1.4, 900);
        AddGlow(Scene, left + screen.Width * .94, top + screen.Height * .53, 65, true, abyssGlows, 3.2, 900);
        AddGlow(Scene, left + screen.Width * .15, top + screen.Height * .925, 56, false, abyssGlows, 5.2, 900);
        AddGlow(Scene, left + screen.Width * .40, top + screen.Height * .936, 52, true, abyssGlows, 1.9, 900);
        AddGlow(Scene, left + screen.Width * .58, top + screen.Height * .927, 56, false, abyssGlows, 4.4, 900);
        AddGlow(Scene, left + screen.Width * .85, top + screen.Height * .93, 58, true, abyssGlows, .8, 900);
        AddGlow(Scene, left + screen.Width * .1, top + screen.Height * .012, 48, true, abyssGlows, 3.7, 900);
        AddGlow(Scene, left + screen.Width * .88, top + screen.Height * .014, 48, false, abyssGlows, 2.6, 900);
    }

    private void DrawAbyssTitle(Drawing.Rectangle screen)
    {
        var width = Math.Min(720, screen.Width * .68);
        var height = width / 3;
        var x = screen.Left - virtualScreen.Left + (screen.Width - width) / 2;
        var y = screen.Top - virtualScreen.Top - 8;
        var plaque = new Image
        {
            Source = AbyssPlaque, Width = width, Height = height,
            Stretch = Stretch.Fill, IsHitTestVisible = false
        };
        Canvas.SetLeft(plaque, x);
        Canvas.SetTop(plaque, y);
        Canvas.SetZIndex(plaque, 997);
        Scene.Children.Add(plaque);
        AddMaterialPulse(Scene, plaque, 1.9);
        AddGlow(Scene, x + width * .09, y + height * .24, 65, true, abyssGlows, 1.3, 998);
        AddGlow(Scene, x + width * .82, y + height * .24, 65, false, abyssGlows, 3.6, 998);
        var titleGlow = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = MediaColor.FromRgb(238, 32, 64), BlurRadius = 22,
            ShadowDepth = 0, Opacity = 1
        };
        var title = new TextBlock
        {
            Text = "DANGER", Width = width * .54, TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily("Old English Text MT"), FontSize = Math.Clamp(width * .082, 33, 59),
            FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.NoWrap,
            IsHitTestVisible = false, Effect = titleGlow
        };
        Canvas.SetLeft(title, x + width * .23);
        Canvas.SetTop(title, y + height * .30);
        Canvas.SetZIndex(title, 1000);
        Scene.Children.Add(title);
        abyssTitles.Add((plaque, title, titleGlow));
    }

    private static void AddGlow(Canvas canvas, double x, double y, double size, bool red,
        List<(Ellipse Glow, bool Red, double Phase)> list, double phase, int z = 2)
    {
        var color = red ? MediaColor.FromArgb(242, 245, 20, 58) : MediaColor.FromArgb(234, 28, 248, 130);
        var glow = new Ellipse
        {
            Width = size, Height = size * .68, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(color, 0),
                new(MediaColor.FromArgb(94, color.R, color.G, color.B), .52),
                new(MediaColor.FromArgb(0, color.R, color.G, color.B), 1)
            })
        };
        Canvas.SetLeft(glow, x);
        Canvas.SetTop(glow, y);
        Canvas.SetZIndex(glow, z);
        canvas.Children.Add(glow);
        list.Add((glow, red, phase));
    }

    private static void AddMaterialPulse(Canvas canvas, Image art, double phase)
    {
        var mask = new ImageBrush(art.Source) { Stretch = Stretch.Fill };
        mask.Freeze();
        var emission = GetEmissionMasks((BitmapSource)art.Source);
        var shade = new System.Windows.Shapes.Rectangle
        {
            Width = art.Width, Height = art.Height, Fill = Brushes.Black,
            OpacityMask = mask, IsHitTestVisible = false,
            RenderTransformOrigin = art.RenderTransformOrigin,
            RenderTransform = art.RenderTransform
        };
        Canvas.SetLeft(shade, Canvas.GetLeft(art));
        Canvas.SetTop(shade, Canvas.GetTop(art));
        Canvas.SetZIndex(shade, Canvas.GetZIndex(art));
        canvas.Children.Add(shade);
        shade.BeginAnimation(OpacityProperty, new DoubleAnimation(.05, .77, TimeSpan.FromSeconds(2.1 + phase * .3))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
        // Colored pockets are clipped to the same silhouette as the rocks;
        // the underlying desktop never acquires a rectangular tint.
        for (var i = 0; i < 2; i++)
        {
            var red = i == 0;
            var color = red ? MediaColor.FromArgb(220, 255, 13, 43) : MediaColor.FromArgb(210, 19, 255, 105);
            var light = new System.Windows.Shapes.Rectangle
            {
                Width = art.Width, Height = art.Height, OpacityMask = red ? emission.Red : emission.Green,
                IsHitTestVisible = false, RenderTransformOrigin = art.RenderTransformOrigin,
                RenderTransform = art.RenderTransform,
                Fill = new SolidColorBrush(color)
            };
            Canvas.SetLeft(light, Canvas.GetLeft(art));
            Canvas.SetTop(light, Canvas.GetTop(art));
            Canvas.SetZIndex(light, Canvas.GetZIndex(art));
            canvas.Children.Add(light);
            light.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(3.4 + phase * .17), RepeatBehavior = RepeatBehavior.Forever,
                KeyFrames =
                [
                    new LinearDoubleKeyFrame(red ? .04 : .48, KeyTime.FromPercent(0)),
                    new LinearDoubleKeyFrame(red ? .8 : .06, KeyTime.FromPercent(.35)),
                    new LinearDoubleKeyFrame(.08, KeyTime.FromPercent(.49)),
                    new LinearDoubleKeyFrame(red ? .45 : .85, KeyTime.FromPercent(.65)),
                    new LinearDoubleKeyFrame(red ? .04 : .48, KeyTime.FromPercent(1))
                ]
            });
        }
    }

    private static (ImageBrush Red, ImageBrush Green) GetEmissionMasks(BitmapSource source)
    {
        if (EmissionMasks.TryGetValue(source, out var cached)) return cached;
        var rgba = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = rgba.PixelWidth * 4;
        var pixels = new byte[stride * rgba.PixelHeight];
        rgba.CopyPixels(pixels, stride, 0);
        ImageBrush Create(bool red)
        {
            var alpha = new byte[pixels.Length];
            for (var p = 0; p < pixels.Length; p += 4)
            {
                var b = pixels[p]; var g = pixels[p + 1]; var r = pixels[p + 2];
                var intensity = red ? r - Math.Max(g, b) * 1.1 : g - Math.Max(r, b) * 1.04;
                alpha[p] = alpha[p + 1] = alpha[p + 2] = 255;
                alpha[p + 3] = (byte)(pixels[p + 3] * Math.Clamp((intensity - 8) / 75, 0, 1));
            }
            var bitmap = BitmapSource.Create(rgba.PixelWidth, rgba.PixelHeight, 96, 96, PixelFormats.Bgra32, null, alpha, stride);
            bitmap.Freeze();
            var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
            brush.Freeze();
            return brush;
        }
        var result = (Create(true), Create(false));
        EmissionMasks.Add(source, result);
        return result;
    }

    private void DrawRootFrame(Drawing.Rectangle screen)
    {
        var left = screen.Left - virtualScreen.Left;
        var top = screen.Top - virtualScreen.Top;
        AddEdgeVeil(left, top, screen.Width, 150, false, false);
        AddEdgeVeil(left, top + screen.Height - 150, screen.Width, 150, false, true);
        AddEdgeVeil(left, top, 135, screen.Height, true, false);
        AddEdgeVeil(left + screen.Width - 135, top, 135, screen.Height, true, true);
        for (var x = 12; x < screen.Width - 18;)
        {
            var length = Math.Min(random.Next(100, 245), screen.Width - x - 12);
            DrawRootSegment(left + x, top + random.Next(5, 17), length, true, 1, random.Next(2) == 0);
            DrawRootSegment(left + x + random.Next(-20, 21), top + screen.Height - random.Next(5, 17), length * random.NextDouble() * .35 + length * .72, true, -1, random.Next(2) == 0);
            x += (int)length + random.Next(26, 65);
        }
        for (var y = 12; y < screen.Height - 18;)
        {
            var length = Math.Min(random.Next(90, 185), screen.Height - y - 12);
            DrawRootSegment(left + random.Next(5, 17), top + y, length, false, 1, random.Next(2) == 0);
            DrawRootSegment(left + screen.Width - random.Next(5, 17), top + y + random.Next(-12, 13), length, false, -1, random.Next(2) == 0);
            y += (int)length + random.Next(20, 52);
        }
    }

    private void AddEdgeVeil(double x, double y, double width, double height, bool horizontal, bool reverse)
    {
        var dark = MediaColor.FromArgb(219, 0, 1, 3);
        var clear = MediaColor.FromArgb(0, 0, 1, 3);
        var veil = new System.Windows.Shapes.Rectangle
        {
            Width = width, Height = height, IsHitTestVisible = false,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(reverse ? clear : dark, 0),
                    new(reverse ? dark : clear, 1)
                },
                new Point(0, 0), horizontal ? new Point(1, 0) : new Point(0, 1))
        };
        Canvas.SetLeft(veil, x);
        Canvas.SetTop(veil, y);
        Canvas.SetZIndex(veil, 880);
        Scene.Children.Add(veil);
    }

    private void DrawRootSegment(double x, double y, double length, bool horizontal, int inward, bool red)
    {
        Point At(double along, double depth) => horizontal
            ? new Point(x + along, y + depth * inward)
            : new Point(x + depth * inward, y + along);
        AddRootLine(new PointCollection
        {
            At(0, 0), At(length * .17, 5), At(length * .38, -4),
            At(length * .61, 8), At(length * .83, -3), At(length, 0)
        }, red, 5.1);
        var stem = length * (.35 + random.NextDouble() * .35);
        var rootDepth = random.Next(70, 145);
        AddRootLine(new PointCollection
        {
            At(stem, 1), At(stem + 13, 15), At(stem - 5, 33),
            At(stem + random.Next(-26, 27), rootDepth * .64),
            At(stem + random.Next(-40, 41), rootDepth)
        }, red, 3.1);
        if (random.NextDouble() < .57)
            AddRootLine(new PointCollection { At(stem + 10, 16), At(stem + 35, 29), At(stem + 21, 48) }, !red, 1.1);
        if (rootDepth > 90)
            AddRootLine(new PointCollection
            {
                At(stem - 5, 33), At(stem - random.Next(18, 42), rootDepth * .58),
                At(stem - random.Next(35, 75), rootDepth * .8)
            }, red, 1.2);
        for (var i = 0; i < 2; i++)
        {
            var baseAlong = stem + random.Next(-29, 30);
            var depth = rootDepth * (.35 + i * .23);
            var side = i == 0 ? -1 : 1;
            var thorn = new Polygon
            {
                Points = new PointCollection
                {
                    At(baseAlong - 8, depth),
                    At(baseAlong + 9, depth + 5),
                    At(baseAlong + side * random.Next(35, 70), depth + random.Next(24, 58))
                },
                Fill = new SolidColorBrush(MediaColor.FromArgb(240, 1, 3, 5)),
                Stroke = red ? RedDim : GreenDim,
                StrokeThickness = .8, IsHitTestVisible = false
            };
            Canvas.SetZIndex(thorn, 900);
            Scene.Children.Add(thorn);
        }
    }

    private void AddRootLine(PointCollection points, bool red, double thickness)
    {
        var shadow = new Polyline
        {
            Points = points, Stroke = new SolidColorBrush(MediaColor.FromArgb(238, 0, 2, 5)),
            StrokeThickness = thickness + 8, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
        };
        Canvas.SetZIndex(shadow, 900);
        Scene.Children.Add(shadow);
        var line = new Polyline
        {
            Points = points, StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round,
            Stroke = new SolidColorBrush(red ? MediaColor.FromRgb(112, 18, 32) : MediaColor.FromRgb(19, 98, 65)),
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(line, 901);
        Scene.Children.Add(line);
        roots.Add((line, red));
    }

    private void DrawTitle(Drawing.Rectangle screen)
    {
        var width = Math.Min(560, screen.Width - 40);
        var x = screen.Left - virtualScreen.Left + (screen.Width - width) / 2;
        var y = screen.Top - virtualScreen.Top + 16;
        var shroud = new Ellipse
        {
            Width = width + 230, Height = 158,
            Fill = new SolidColorBrush(MediaColor.FromArgb(234, 0, 0, 1)),
            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 37 },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(shroud, x - 115);
        Canvas.SetTop(shroud, y - 22);
        Canvas.SetZIndex(shroud, 997);
        Scene.Children.Add(shroud);
        var frame = new Polygon
        {
            Width = width, Height = 112,
            Points = new PointCollection
            {
                new(0, 42), new(24, 18), new(47, 23), new(70, 0),
                new(99, 16), new(width * .29, 5), new(width * .36, 18),
                new(width * .5, 0), new(width * .64, 18), new(width * .71, 5),
                new(width - 99, 16), new(width - 70, 0), new(width - 47, 23),
                new(width - 24, 18), new(width, 42), new(width - 14, 69),
                new(width - 38, 84), new(width - 72, 80), new(width - 105, 111),
                new(width * .68, 91), new(width * .5, 106), new(width * .32, 91),
                new(105, 111), new(72, 80), new(38, 84), new(14, 69)
            },
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(MediaColor.FromArgb(250, 1, 2, 4), 0),
                    new(MediaColor.FromArgb(253, 15, 2, 9), .48),
                    new(MediaColor.FromArgb(253, 0, 1, 3), 1)
                }, new Point(0, 0), new Point(0, 1)),
            StrokeThickness = 3.2, IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = MediaColor.FromRgb(0, 0, 0), BlurRadius = 30,
                ShadowDepth = 12, Opacity = 1
            }
        };
        Canvas.SetLeft(frame, x);
        Canvas.SetTop(frame, y);
        Canvas.SetZIndex(frame, 1000);
        Scene.Children.Add(frame);
        for (var side = 0; side < 2; side++)
        {
            var leftSide = side == 0;
            var sign = leftSide ? -1 : 1;
            var edge = leftSide ? x + 38 : x + width - 38;
            var claw = new Polyline
            {
                Points = new PointCollection(leftSide
                    ? new[] { new Point(edge, y + 28), new Point(x - 30, y + 2), new Point(x - 83, y + 48), new Point(x - 139, y + 19) }
                    : new[] { new Point(edge, y + 28), new Point(x + width + 30, y + 2), new Point(x + width + 83, y + 48), new Point(x + width + 139, y + 19) }),
                Stroke = new SolidColorBrush(MediaColor.FromRgb(1, 3, 5)),
                StrokeThickness = 19, StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            Canvas.SetZIndex(claw, 998);
            Scene.Children.Add(claw);
            var vein = new Polyline
            {
                Points = claw.Points,
                Stroke = leftSide ? RedDim : GreenDim,
                StrokeThickness = 2.2, IsHitTestVisible = false
            };
            Canvas.SetZIndex(vein, 999);
            Scene.Children.Add(vein);
            var socket = new Ellipse
            {
                Width = 38, Height = 26,
                Fill = new RadialGradientBrush(MediaColor.FromRgb(37, 4, 15), MediaColor.FromRgb(0, 0, 1)),
                Stroke = new SolidColorBrush(MediaColor.FromRgb(4, 5, 7)),
                StrokeThickness = 4, IsHitTestVisible = false
            };
            Canvas.SetLeft(socket, edge + sign * 2 - 19);
            Canvas.SetTop(socket, y + 42);
            Canvas.SetZIndex(socket, 1001);
            Scene.Children.Add(socket);
            var eye = new Ellipse
            {
                Width = 12, Height = 17,
                Fill = leftSide ? RedBright : GreenBright,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = leftSide ? RedBright.Color : GreenBright.Color,
                    BlurRadius = 14, ShadowDepth = 0, Opacity = .8
                },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(eye, edge - 6);
            Canvas.SetTop(eye, y + 46);
            Canvas.SetZIndex(eye, 1002);
            Scene.Children.Add(eye);
            eyes.Add(eye);
        }
        for (var i = 0; i < 13; i++)
        {
            var position = 86 + i * (width - 172) / 12;
            var topFang = new Polygon
            {
                Points = new PointCollection
                {
                    new(position - 11, y + 17), new(position + 13, y + 16),
                    new(position + (i % 2 == 0 ? -5 : 5), y + 36 + random.Next(3, 17))
                },
                Fill = new SolidColorBrush(MediaColor.FromArgb(252, 0, 1, 3)),
                IsHitTestVisible = false
            };
            Canvas.SetZIndex(topFang, 1001);
            Scene.Children.Add(topFang);
            var lowerFang = new Polygon
            {
                Points = new PointCollection
                {
                    new(position - 11, y + 91), new(position + 13, y + 91),
                    new(position + (i % 2 == 0 ? 5 : -5), y + 69 - random.Next(3, 16))
                },
                Fill = new SolidColorBrush(MediaColor.FromArgb(248, 0, 1, 3)),
                IsHitTestVisible = false
            };
            Canvas.SetZIndex(lowerFang, 1001);
            Scene.Children.Add(lowerFang);
        }
        var title = new TextBlock
        {
            Text = "DANGER", Width = width, TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"), FontSize = 35,
            FontWeight = FontWeights.Black, IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = MediaColor.FromRgb(124, 7, 27), BlurRadius = 22,
                ShadowDepth = 0, Opacity = .9
            }
        };
        Canvas.SetLeft(title, x);
        Canvas.SetTop(title, y + 32);
        Canvas.SetZIndex(title, 1003);
        Scene.Children.Add(title);
        titles.Add((title, frame));
    }

    private void Pulse()
    {
        frame++;
        var redPhase = frame % 38 < 20;
        if (frame % 5 == 1)
        {
            foreach (var (shape, red) in roots)
            {
                var active = red == redPhase;
                shape.Stroke = red ? (active ? RedBright : RedDim) : (active ? GreenBright : GreenDim);
                shape.Opacity = active ? .43 + .25 * Math.Abs(Math.Sin(frame * .071)) : .28;
            }
        }
        var messages = new[] { "DANGER", "VIRUS", "HACKER" };
        foreach (var (title, border) in titles)
        {
            var message = messages[(frame / 17) % messages.Length];
            if (title.Text != message) title.Text = message;
            var brush = redPhase ? RedBright : GreenBright;
            title.Foreground = brush;
            title.Opacity = frame % 31 is 0 or 1 or 4 ? .13 : .87;
            border.Stroke = brush;
            border.Opacity = .48 + .27 * Math.Abs(Math.Sin(frame * .073));
        }
        foreach (var (plaque, title, glow) in abyssTitles)
        {
            var message = messages[(frame / 17) % messages.Length];
            if (title.Text != message) title.Text = message;
            var titleColor = redPhase ? MediaColor.FromRgb(245, 46, 76) : MediaColor.FromRgb(62, 234, 136);
            title.Foreground = new SolidColorBrush(titleColor);
            glow.Color = titleColor;
            glow.Opacity = .48 + .52 * Math.Abs(Math.Sin(frame * .19));
            title.Opacity = frame % 31 is 0 or 1 or 4 ? .24 : .88 + .12 * Math.Abs(Math.Sin(frame * .17));
            plaque.Opacity = .92 + .08 * Math.Abs(Math.Sin(frame * .08));
        }
        foreach (var (glow, _, phase) in abyssGlows)
            glow.Opacity = .20 + .80 * Math.Pow(Math.Max(0, Math.Sin(frame * .16 + phase)), 2);
        foreach (var (glow, _, phase) in impactGlows)
            glow.Opacity = .1 + .82 * Math.Pow(Math.Max(0, Math.Sin(frame * .22 + phase)), 2);
        for (var i = 0; i < eyes.Count; i++)
        {
            var blink = (frame + i * 23) % 89 is 0 or 1 or 2;
            eyes[i].Opacity = blink ? .02 : .45 + .44 * Math.Abs(Math.Sin(frame * .085 + i));
        }
    }

    public void StopEffect()
    {
        pulse.Stop();
        heatHaze.Stop();
        if (IsVisible) Hide();
        Scene.Children.Clear();
        fractures.Clear();
        roots.Clear();
        titles.Clear();
        eyes.Clear();
        abyssTitles.Clear();
        abyssGlows.Clear();
        impactGlows.Clear();
        snapshots.Clear();
        impactsSinceHealing = 0;
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
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint handle);
}
