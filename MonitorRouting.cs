using System.Drawing;
using Forms = System.Windows.Forms;

namespace FirawH4K;

internal static class MonitorRouting
{
    private static HashSet<string>? enabledDevices;

    public static void SetEnabledDevices(IEnumerable<string> devices) =>
        enabledDevices = new HashSet<string>(devices, StringComparer.OrdinalIgnoreCase);

    public static string[]? GetEnabledDevices() => enabledDevices?.ToArray();

    public static Rectangle[] ActiveDesktopScreens() =>
        Forms.Screen.AllScreens
            .Where(screen => enabledDevices == null || enabledDevices.Contains(screen.DeviceName))
            .Select(screen => screen.Bounds)
            .Where(bounds => bounds.Width >= 260 && bounds.Height >= 260)
            .Distinct()
            .ToArray();

    public static bool IsActive(Rectangle bounds) => ActiveDesktopScreens().Contains(bounds);

    public static Rectangle ActiveDesktopBounds()
    {
        var screens = ActiveDesktopScreens();
        if (screens.Length == 0)
            return Forms.Screen.PrimaryScreen?.Bounds ?? Forms.SystemInformation.VirtualScreen;
        var bounds = screens[0];
        foreach (var screen in screens.Skip(1))
            bounds = Rectangle.Union(bounds, screen);
        return bounds;
    }

    public static bool TryChooseNeighbour(Rectangle current, Random random, out Rectangle destination, out double x, out double y)
    {
        var neighbours = ActiveDesktopScreens()
            .Where(screen => screen != current)
            .Select(screen => (Screen: screen, Gateway: Gateway(current, screen)))
            .Where(item => item.Gateway.HasValue)
            .ToArray();
        if (neighbours.Length == 0)
        {
            destination = current;
            x = y = 0;
            return false;
        }

        var selected = neighbours[random.Next(neighbours.Length)];
        destination = selected.Screen;
        x = selected.Gateway!.Value.X;
        y = selected.Gateway.Value.Y;
        return true;
    }

    public static bool TryNextHop(Rectangle current, Rectangle goal, out Rectangle next, out double x, out double y)
    {
        next = current;
        x = y = 0;
        if (current == goal) return false;
        var screens = ActiveDesktopScreens();
        var queue = new Queue<Rectangle>();
        var previous = new Dictionary<Rectangle, Rectangle>();
        queue.Enqueue(current);
        previous[current] = current;
        while (queue.Count > 0)
        {
            var screen = queue.Dequeue();
            foreach (var candidate in screens)
            {
                if (previous.ContainsKey(candidate) || Gateway(screen, candidate) == null) continue;
                previous[candidate] = screen;
                if (candidate == goal)
                {
                    next = candidate;
                    while (previous[next] != current)
                        next = previous[next];
                    var gateway = Gateway(current, next)!.Value;
                    x = gateway.X;
                    y = gateway.Y;
                    return true;
                }
                queue.Enqueue(candidate);
            }
        }
        return false;
    }

    private static (double X, double Y)? Gateway(Rectangle from, Rectangle to)
    {
        const int borderTolerance = 3;
        const int minimumSharedEdge = 130;
        var verticalTop = Math.Max(from.Top, to.Top);
        var verticalBottom = Math.Min(from.Bottom, to.Bottom);
        if (verticalBottom - verticalTop >= minimumSharedEdge)
        {
            var y = (verticalTop + verticalBottom) / 2.0;
            if (Math.Abs(from.Right - to.Left) <= borderTolerance)
                return (to.Left + Math.Min(125, to.Width / 3), y);
            if (Math.Abs(from.Left - to.Right) <= borderTolerance)
                return (to.Right - Math.Min(125, to.Width / 3), y);
        }

        var horizontalLeft = Math.Max(from.Left, to.Left);
        var horizontalRight = Math.Min(from.Right, to.Right);
        if (horizontalRight - horizontalLeft >= minimumSharedEdge)
        {
            var x = (horizontalLeft + horizontalRight) / 2.0;
            if (Math.Abs(from.Bottom - to.Top) <= borderTolerance)
                return (x, to.Top + Math.Min(125, to.Height / 3));
            if (Math.Abs(from.Top - to.Bottom) <= borderTolerance)
                return (x, to.Bottom - Math.Min(125, to.Height / 3));
        }
        return null;
    }
}
