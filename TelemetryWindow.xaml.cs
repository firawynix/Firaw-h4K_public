using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FirawH4K;

public partial class TelemetryWindow : Window
{
    private readonly DispatcherTimer sampleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Queue<double> cpuHistory = new();
    private long pullStarted;
    private bool pulling;
    private double startX, startY, endX, endY;
    private ulong previousIdle, previousKernel, previousUser;

    public TelemetryWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        sampleTimer.Tick += (_, _) => Sample();
        Closed += (_, _) => { StopPull(); sampleTimer.Stop(); };
    }

    public void EmergeAt(double x, double y, System.Drawing.Rectangle bounds, IEnumerable<Rect>? occupied = null)
    {
        const double panelWidth = 490;
        const double panelHeight = 318;
        startX = x - panelWidth / 2;
        startY = y - panelHeight / 2;
        var left = bounds.Left + 22;
        var right = Math.Max(left, bounds.Right - panelWidth - 22);
        var top = bounds.Top + 32;
        var bottom = Math.Max(top, bounds.Bottom - panelHeight - 28);
        var obstacles = occupied?.ToArray() ?? [];
        var candidates = new[] { (X: left, Y: top), (X: right, Y: top), (X: left, Y: bottom), (X: right, Y: bottom) };
        var best = candidates.OrderBy(candidate =>
        {
            var placement = new Rect(candidate.X - 15, candidate.Y - 15, panelWidth + 30, panelHeight + 30);
            var overlap = obstacles.Sum(obstacle =>
            {
                var intersection = Rect.Intersect(placement, obstacle);
                return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
            });
            var distance = Math.Sqrt(Math.Pow(candidate.X + panelWidth / 2 - x, 2) + Math.Pow(candidate.Y + panelHeight / 2 - y, 2));
            return overlap * 30 + distance * 0.1;
        }).First();
        endX = best.X;
        endY = best.Y;
        StopPull();
        sampleTimer.Stop();
        Left = startX;
        Top = startY;
        MonitorScale.ScaleX = 0.06;
        MonitorScale.ScaleY = 0.06;
        MonitorRotation.Angle = -13;
        MonitorSkew.AngleX = -8;
        MonitorRoot.Opacity = 0;
        AssemblyCanvas.Opacity = 0;
        Sample();
        MonitorPanel.CacheMode = new BitmapCache();
        if (!IsVisible) Show();
        pullStarted = Stopwatch.GetTimestamp();
        pulling = true;
        CompositionTarget.Rendering += AnimatePull;
    }

    public void HideMonitor()
    {
        StopPull();
        sampleTimer.Stop();
        Hide();
    }

    private void AnimatePull(object? sender, EventArgs e)
    {
        if (!pulling) return;
        var t = Math.Clamp(Stopwatch.GetElapsedTime(pullStarted).TotalMilliseconds / 1800, 0, 1);
        var eased = t * t * (3 - 2 * t);
        Left = startX + (endX - startX) * eased;
        Top = startY + (endY - startY) * eased;
        MonitorScale.ScaleX = 0.06 + 0.94 * eased;
        MonitorScale.ScaleY = 0.06 + 0.94 * eased;
        MonitorRotation.Angle = -13 * (1 - eased) + Math.Sin(t * 32) * (1 - t) * 2.8;
        MonitorSkew.AngleX = -8 * (1 - eased) + Math.Sin(t * 47) * (1 - t) * 1.7;
        MonitorRoot.Opacity = Math.Min(1, t * 4);
        var assembly = Math.Clamp((t - 0.42) / 0.47, 0, 1);
        AssemblyCanvas.Opacity = assembly <= 0 ? 0 : Math.Min(1, assembly * 5) * Math.Min(1, (1 - t) * 10);
        System.Windows.Controls.Canvas.SetTop(AssemblyTop, 15 - 55 * (1 - assembly));
        System.Windows.Controls.Canvas.SetTop(AssemblyBottom, 301 + 55 * (1 - assembly));
        System.Windows.Controls.Canvas.SetLeft(AssemblyLeft, 11 - 65 * (1 - assembly));
        System.Windows.Controls.Canvas.SetLeft(AssemblyRight, 476 + 65 * (1 - assembly));
        if (t >= 1)
        {
            StopPull();
            sampleTimer.Start();
        }
    }

    private void StopPull()
    {
        pulling = false;
        CompositionTarget.Rendering -= AnimatePull;
        MonitorPanel.CacheMode = null;
    }

    private void Sample()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var idleValue = idle.Value;
            var kernelValue = kernel.Value;
            var userValue = user.Value;
            if (previousKernel != 0)
            {
                var idleDelta = idleValue - previousIdle;
                var totalDelta = kernelValue - previousKernel + userValue - previousUser;
                if (totalDelta > 0)
                {
                    var usage = Math.Clamp(100 * (1 - (double)idleDelta / totalDelta), 0, 100);
                    CpuText.Text = $"{usage:0}%";
                    cpuHistory.Enqueue(usage);
                    while (cpuHistory.Count > 34) cpuHistory.Dequeue();
                    CpuGraph.Points = new PointCollection(cpuHistory.Select((value, index) => new System.Windows.Point(index * 6.2, 31 - value * 0.29)));
                }
            }
            previousIdle = idleValue;
            previousKernel = kernelValue;
            previousUser = userValue;
        }

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0)
        {
            var used = memory.TotalPhysical - memory.AvailablePhysical;
            MemoryText.Text = $"{used * 100.0 / memory.TotalPhysical:0}%";
            MemoryDetail.Text = $"{used / 1073741824.0:0.0} / {memory.TotalPhysical / 1073741824.0:0.0} GB";
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

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; public ulong Value => ((ulong)High << 32) | Low; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("user32.dll")] private static extern nint GetWindowLong(nint handle, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLong(nint handle, int index, nint value);
}
