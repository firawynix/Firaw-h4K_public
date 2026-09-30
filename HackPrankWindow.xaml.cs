using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;

namespace FirawH4K;

public partial class HackPrankWindow : Window
{
    private readonly DispatcherTimer outputTimer = new() { Interval = TimeSpan.FromMilliseconds(430) };
    private readonly Queue<string> lines = new();
    private double targetLeft;
    private double targetTop;
    private double startLeft;
    private double startTop;
    private long pullStartedAt;
    private bool pulling;
    private int outputStep;
    private bool redTheme;
    private static readonly Lazy<Task<string>> GreenArt = new(() => Task.Run(() => BuildAsciiArt("cyber-beetle-h4k-dark.png")));
    private static readonly Lazy<Task<string>> RedArt = new(() => Task.Run(() => BuildAsciiArt("cyber-beetle-red-dark.png")));
    private int artVersion;

    private static readonly string[] GreenMessages =
    [
        "[H4K] matrix.feed   sincronizado com a tela",
        "[H4K] beetle.trace  pegadas holográficas ativas",
        "[H4K] visual.cache  0xA4F2 recebido",
        "[H4K] shell.frame   renderizando glifos",
        "[H4K] display.node  fluxo estável"
    ];

    private static readonly string[] RedMessages =
    [
        "[H4K] red.agent    emergiu da fissura",
        "[H4K] glyph.stream fluxo carmesim ativo",
        "[H4K] phantom.cache 0xD31A recebido",
        "[H4K] display.node janela extraída",
        "[H4K] red.trace    rastro estabilizado"
    ];

    public HackPrankWindow()
    {
        InitializeComponent();
        SourceInitialized += HackPrankWindow_SourceInitialized;
        outputTimer.Tick += OutputTimer_Tick;
        Closed += (_, _) => { StopPull(); outputTimer.Stop(); };
        _ = GreenArt.Value;
        _ = RedArt.Value;
    }

    public void ConfigurePanel(int panelIndex, bool redTheme)
    {
        this.redTheme = redTheme;
        var accent = redTheme ? MediaColor.FromRgb(204, 48, 64) : MediaColor.FromRgb(42, 173, 98);
        var pale = redTheme ? MediaColor.FromRgb(255, 164, 171) : MediaColor.FromRgb(151, 255, 180);
        ShellBorder.BorderBrush = new SolidColorBrush(accent);
        TitleText.Foreground = new SolidColorBrush(pale);
        LiveText.Foreground = new SolidColorBrush(accent);
        AsciiArt.Foreground = new SolidColorBrush(redTheme ? MediaColor.FromRgb(239, 67, 80) : MediaColor.FromRgb(58, 196, 113));
        PromptText.Foreground = new SolidColorBrush(pale);
        LogText.Foreground = new SolidColorBrush(accent);
        ProgressText.Foreground = new SolidColorBrush(pale);
        TitleText.Text = redTheme ? "FIRAW SHELL // INFILTRATOR 02" : "FIRAW SHELL // BEETLE 01";
        Title = TitleText.Text;
        PromptText.Text = redTheme
            ? "infiltrator@display:~$ ./red-agent --visual"
            : "firaw@display:~$ ./beetle --visual";
        _ = ApplyAsciiArtAsync(redTheme, ++artVersion);
        outputStep = 0;
        lines.Clear();
        lines.Enqueue("[H4K] inicializando terminal visual...");
        LogText.Text = string.Join("\n", lines);
        ProgressText.Text = "[░░░░░░░░░░░░░░░░░░░░] 00%  // visual only";
    }

    public void MoveAway(int x, int y, bool show, System.Drawing.Rectangle bounds, int panelIndex)
    {
        if (IsVisible || !show)
            return;

        const int width = 690;
        const int height = 530;
        var leftSpace = Math.Abs(x - bounds.Left);
        var rightSpace = Math.Abs(bounds.Right - x);
        var topSpace = Math.Abs(y - bounds.Top);
        var bottomSpace = Math.Abs(bounds.Bottom - y);
        var edge = Math.Min(Math.Min(leftSpace, rightSpace), Math.Min(topSpace, bottomSpace));
        var minX = bounds.Left + 12;
        var maxX = Math.Max(minX, bounds.Right - width - 12);
        var minY = bounds.Top + 36;
        var maxY = Math.Max(minY, bounds.Bottom - height - 12);

        if (edge == leftSpace)
        {
            targetLeft = Math.Clamp(bounds.Left + 130, minX, maxX);
            targetTop = Math.Clamp(panelIndex == 0 ? bounds.Top + 36 : bounds.Bottom - height - 12, minY, maxY);
            startLeft = bounds.Left - width - 12;
            startTop = Math.Clamp(y - height / 2.0, minY, maxY);
        }
        else if (edge == rightSpace)
        {
            targetLeft = Math.Clamp(bounds.Right - width - 130, minX, maxX);
            targetTop = Math.Clamp(panelIndex == 0 ? bounds.Top + 36 : bounds.Bottom - height - 12, minY, maxY);
            startLeft = bounds.Right + 12;
            startTop = Math.Clamp(y - height / 2.0, minY, maxY);
        }
        else if (edge == topSpace)
        {
            targetLeft = Math.Clamp(panelIndex == 0 ? bounds.Left + 52 : bounds.Right - width - 52, minX, maxX);
            targetTop = Math.Clamp(bounds.Top + 130, minY, maxY);
            startLeft = Math.Clamp(x - width / 2.0, minX, maxX);
            startTop = bounds.Top - height - 12;
        }
        else
        {
            targetLeft = Math.Clamp(panelIndex == 0 ? bounds.Left + 52 : bounds.Right - width - 52, minX, maxX);
            targetTop = Math.Clamp(bounds.Bottom - height - 130, minY, maxY);
            startLeft = Math.Clamp(x - width / 2.0, minX, maxX);
            startTop = bounds.Bottom + 12;
        }

        StopPull();
        Left = startLeft;
        Top = startTop;
        ShellBorder.CacheMode = new BitmapCache();
        Show();
        pullStartedAt = Stopwatch.GetTimestamp();
        pulling = true;
        CompositionTarget.Rendering += AnimatePull;
    }

    public void HidePrank()
    {
        StopPull();
        outputTimer.Stop();
        Hide();
    }

    private void AnimatePull(object? sender, EventArgs e)
    {
        if (!pulling) return;
        var t = Math.Clamp(Stopwatch.GetElapsedTime(pullStartedAt).TotalMilliseconds / 550.0, 0, 1);
        var eased = t * t * (3 - 2 * t);
        Left = startLeft + (targetLeft - startLeft) * eased;
        Top = startTop + (targetTop - startTop) * eased;
        if (t >= 1)
        {
            Left = targetLeft;
            Top = targetTop;
            StopPull();
            outputTimer.Start();
        }
    }

    private void StopPull()
    {
        pulling = false;
        CompositionTarget.Rendering -= AnimatePull;
        ShellBorder.CacheMode = null;
    }

    private async Task ApplyAsciiArtAsync(bool red, int version)
    {
        try
        {
            var art = await (red ? RedArt.Value : GreenArt.Value);
            if (version == artVersion) AsciiArt.Text = art;
        }
        catch (Exception)
        {
            if (version == artVersion) AsciiArt.Text = "[ FIRAW // BEETLE ]";
        }
    }

    private void OutputTimer_Tick(object? sender, EventArgs e)
    {
        var messages = redTheme ? RedMessages : GreenMessages;
        lines.Enqueue(messages[outputStep % messages.Length]);
        while (lines.Count > 4)
            lines.Dequeue();
        LogText.Text = string.Join("\n", lines);
        var progress = (outputStep * 7) % 101;
        var filled = Math.Min(20, progress / 5);
        ProgressText.Text = $"[{new string('█', filled)}{new string('░', 20 - filled)}] {progress:D2}%  // online";
        outputStep++;
    }

    private static string BuildAsciiArt(string assetName)
    {
        var bitmap = new BitmapImage(new Uri($"pack://application:,,,/{typeof(HackPrankWindow).Assembly.GetName().Name};component/Assets/{assetName}"));
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        const int columns = 88;
        const int rows = 30;
        const string ramp = ".:;coO0XK#@";
        var result = new StringBuilder();

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var coverage = 0.0;
                var light = 0.0;
                var low = 1.0;
                var high = 0.0;
                for (var sy = 0; sy < 3; sy++)
                {
                    for (var sx = 0; sx < 3; sx++)
                    {
                        var sourceX = Math.Clamp((int)((column + (sx + 0.5) / 3) * converted.PixelWidth / columns), 0, converted.PixelWidth - 1);
                        var sourceY = Math.Clamp((int)((row + (sy + 0.5) / 3) * converted.PixelHeight / rows), 0, converted.PixelHeight - 1);
                        var offset = sourceY * stride + sourceX * 4;
                        var alpha = pixels[offset + 3] / 255.0;
                        var luminance = (pixels[offset + 2] * 0.25 + pixels[offset + 1] * 0.56 + pixels[offset] * 0.19) / 255.0;
                        coverage += alpha;
                        light += luminance * alpha;
                        if (alpha > 0.2)
                        {
                            low = Math.Min(low, luminance);
                            high = Math.Max(high, luminance);
                        }
                    }
                }
                coverage /= 9;
                if (coverage < 0.12) { result.Append(' '); continue; }
                var mean = light / Math.Max(0.01, coverage * 9);
                var detail = high - low;
                var shade = Math.Clamp((int)(2.5 + mean * 11 + detail * 2 + coverage), 0, ramp.Length - 1);
                result.Append(ramp[shade]);
            }
            if (row < rows - 1)
                result.AppendLine();
        }
        return result.ToString();
    }

    private void HackPrankWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLong(handle, -20);
        const nint WsExTransparent = 0x00000020;
        const nint WsExToolWindow = 0x00000080;
        const nint WsExNoActivate = 0x08000000;
        SetWindowLong(handle, -20, styles | WsExTransparent | WsExToolWindow | WsExNoActivate);
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == 0x0084)
            {
                handled = true;
                return new IntPtr(-1);
            }
            return IntPtr.Zero;
        });
    }

    [DllImport("user32.dll")]
    private static extern nint GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLong(nint hWnd, int nIndex, nint dwNewLong);
}
