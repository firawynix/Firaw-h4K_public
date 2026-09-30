using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace FirawH4K;

// A shared texture with independently moving bands and thermal illumination.
// Native animation clocks avoid rebuilding bitmaps on every frame.
internal sealed class LavaSurface : FrameworkElement
{
    private static readonly BitmapImage Texture = LoadTexture();
    private readonly DrawingGroup scene = new();

    public LavaSurface(double width, double height)
    {
        Width = width;
        Height = height;
        IsHitTestVisible = false;
        Clip = new EllipseGeometry(new Point(width / 2, height / 2), width * .37, height * .36);
        var baseBrush = CreateBrush(width, height);
        baseBrush.Freeze();
        scene.Children.Add(new GeometryDrawing(baseBrush, null, new RectangleGeometry(new Rect(0, 0, width, height))));
        const int bandCount = 12;
        var bandHeight = height / bandCount;
        for (var i = 0; i < bandCount; i++)
        {
            var molten = CreateBrush(width, height);
            var drift = new TranslateTransform();
            drift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-1.2, 1.2, TimeSpan.FromSeconds(2.2 + i * .19))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-i * .2) });
            drift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-.5, .5, TimeSpan.FromSeconds(2.8 + i * .11))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            molten.Transform = drift;
            scene.Children.Add(new GeometryDrawing(molten, null,
                new RectangleGeometry(new Rect(0, i * bandHeight, width, bandHeight))));
        }
        var heat = new DrawingGroup();
        heat.Children.Add(new GeometryDrawing(new RadialGradientBrush(
            System.Windows.Media.Color.FromArgb(90, 255, 91, 5), Colors.Transparent), null,
            new EllipseGeometry(new Point(width * .5, height * .5), width * .32, height * .32)));
        heat.BeginAnimation(DrawingGroup.OpacityProperty,
            new DoubleAnimation(.1, .72, TimeSpan.FromSeconds(1.8))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        scene.Children.Add(heat);
    }

    private static ImageBrush CreateBrush(double width, double height) => new(Texture)
    {
        Stretch = Stretch.Fill,
        ViewportUnits = BrushMappingMode.Absolute,
        Viewport = new Rect(-3, -3, width + 6, height + 6),
        TileMode = TileMode.None
    };

    private static BitmapImage LoadTexture()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/{typeof(LavaSurface).Assembly.GetName().Name};component/Assets/volcanic-lava-v1.png");
        image.DecodePixelWidth = 512;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    protected override void OnRender(DrawingContext context) => context.DrawDrawing(scene);
}
