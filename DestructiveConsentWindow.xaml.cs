using System.Windows;

namespace FirawH4K;

public partial class DestructiveConsentWindow : Window
{
    public DestructiveConsentWindow() => InitializeComponent();

    private void ConsentCheck_Changed(object sender, RoutedEventArgs e) =>
        AcceptButton.Opacity = ConsentCheck.IsChecked == true ? 1 : 0.62;

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConsentCheck.IsChecked != true)
        {
            ConsentCheck.Focus();
            return;
        }
        DialogResult = true;
    }
}
