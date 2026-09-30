using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace FirawH4K;

public partial class StopOverlayWindow : Window
{
    public event EventHandler? StopRequested;

    public StopOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += StopOverlayWindow_SourceInitialized;
    }

    public void MoveToCursor(int x, int y)
    {
        Left = x + 98;
        Top = y + 92;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);

    private void StopOverlayWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLong(handle, -20);
        SetWindowLong(handle, -20, styles | 0x00000080 | 0x08000000);
    }

    [DllImport("user32.dll")]
    private static extern nint GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLong(nint hWnd, int nIndex, nint dwNewLong);
}
