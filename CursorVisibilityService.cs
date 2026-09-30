using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace FirawH4K;

// Replaces the common Windows cursor shapes only while the consented mode is
// active. A separate, windowless copy of this app reloads the user's cursor
// scheme if the main process crashes or stops responding.
internal sealed class CursorVisibilityService : IDisposable
{
    private static readonly uint[] CursorIds =
    [
        32512, 32513, 32514, 32515, 32516, 32642, 32643,
        32644, 32645, 32646, 32648, 32649, 32650
    ];
    private EventWaitHandle? restoredEvent;
    private EventWaitHandle? heartbeat;
    private bool active;

    public bool Start()
    {
        if (active) return true;
        var eventName = $"Local\\FirawH4KCursor-{Guid.NewGuid():N}";
        try
        {
            restoredEvent = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
            heartbeat = new EventWaitHandle(false, EventResetMode.AutoReset, eventName + "-heartbeat");
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executável indisponível.");
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            info.ArgumentList.Add("--firaw-cursor-watchdog");
            info.ArgumentList.Add(Environment.ProcessId.ToString());
            info.ArgumentList.Add(eventName);
            using var watchdog = Process.Start(info);
            if (watchdog == null) throw new InvalidOperationException("Proteção do cursor indisponível.");

            var andMask = new byte[128];
            Array.Fill(andMask, (byte)0xFF);
            var xorMask = new byte[128];
            var blank = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andMask, xorMask);
            if (blank == IntPtr.Zero) throw new InvalidOperationException("Cursor transparente indisponível.");
            try
            {
                foreach (var id in CursorIds)
                {
                    var copy = CopyIcon(blank);
                    if (copy == IntPtr.Zero) throw new InvalidOperationException("Falha ao copiar cursor.");
                    if (!SetSystemCursor(copy, id))
                    {
                        DestroyCursor(copy);
                        throw new InvalidOperationException("Falha ao ocultar cursor.");
                    }
                }
            }
            finally { DestroyCursor(blank); }
            active = true;
            return true;
        }
        catch (Exception)
        {
            if (RestoreSystemCursors())
            {
                restoredEvent?.Set();
                restoredEvent?.Dispose();
                restoredEvent = null;
                heartbeat?.Dispose();
                heartbeat = null;
            }
            else
            {
                // Leave the watchdog alive to retry restoration at its deadline.
                active = true;
            }
            return false;
        }
    }

    public void Stop()
    {
        if (!active && restoredEvent == null) return;
        // Reload the user's cursor scheme before telling the watchdog to exit.
        if (RestoreSystemCursors())
        {
            active = false;
            restoredEvent?.Set();
            restoredEvent?.Dispose();
            restoredEvent = null;
            heartbeat?.Dispose();
            heartbeat = null;
        }
    }

    public void Dispose() => Stop();

    public void KeepAlive()
    {
        if (active) heartbeat?.Set();
    }

    public static bool TryStartWatchdog(string[] args, System.Windows.Application app)
    {
        if (args.Length != 3 || args[0] != "--firaw-cursor-watchdog" || !int.TryParse(args[1], out var parentId))
            return false;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        EventWaitHandle? signal = null;
        try { signal = EventWaitHandle.OpenExisting(args[2]); }
        catch (Exception) { RestoreSystemCursors(); app.Shutdown(); return true; }

        EventWaitHandle pulse;
        try { pulse = EventWaitHandle.OpenExisting(args[2] + "-heartbeat"); }
        catch (Exception) { signal.Dispose(); RestoreSystemCursors(); app.Shutdown(); return true; }
        var lastPulse = Stopwatch.GetTimestamp();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (signal.WaitOne(0))
            {
                timer.Stop();
                signal.Dispose();
                pulse.Dispose();
                app.Shutdown();
                return;
            }
            var parentAlive = false;
            try { using var parent = Process.GetProcessById(parentId); parentAlive = !parent.HasExited; }
            catch (Exception) { }
            if (pulse.WaitOne(0)) lastPulse = Stopwatch.GetTimestamp();
            if (parentAlive && Stopwatch.GetElapsedTime(lastPulse) < TimeSpan.FromSeconds(5)) return;
            RestoreSystemCursors();
            timer.Stop();
            signal.Dispose();
            pulse.Dispose();
            app.Shutdown();
        };
        timer.Start();
        return true;
    }

    private static bool RestoreSystemCursors() => SystemParametersInfo(0x0057, 0, IntPtr.Zero, 0);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateCursor(nint instance, int hotX, int hotY, int width, int height,
        byte[] andMask, byte[] xorMask);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CopyIcon(nint cursor);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemCursor(nint cursor, uint id);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyCursor(nint cursor);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, nint value, uint flags);
}
