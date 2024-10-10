using System.Windows;
using System.Windows.Threading;
using Sheas_Nginx.Wins;

namespace Sheas_Nginx;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e) => new MainWin().Show();

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Error: {e.Exception.Message}");
        e.Handled = true;
    }
}