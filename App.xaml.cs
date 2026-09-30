using System.Windows;

namespace FirawH4K;

public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? instanceMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (CursorVisibilityService.TryStartWatchdog(e.Args, this)) return;
        instanceMutex = new System.Threading.Mutex(true, "Local\\FirawH4K.App", out var firstInstance);
        if (!firstInstance)
        {
            System.Windows.MessageBox.Show("O Firaw h4K já está aberto. Use o ícone do besouro na bandeja para abrir o painel.", "Firaw h4K");
            instanceMutex.Dispose();
            instanceMutex = null;
            Shutdown();
            return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
