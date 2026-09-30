using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace FirawH4K;

public partial class InputShieldWindow : Window
{
    public InputShieldWindow() => InitializeComponent();

    public void ActivateShield()
    {
        var bounds = MonitorRouting.ActiveDesktopBounds();
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        Show();
        Activate();
        Focus();
    }

    private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // The desktop shield only intercepts ordinary input directed at it.
        // OS-reserved shortcuts remain under Windows control.
        e.Handled = true;
    }

    private void Input_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void Input_MouseWheel(object sender, MouseWheelEventArgs e) => e.Handled = true;
}
