using System.Windows;
using System.Windows.Controls;

namespace AiPairLauncher.App.Views;

public partial class LogDrawer : System.Windows.Controls.UserControl
{
    public LogDrawer()
    {
        InitializeComponent();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        ResolveHost()?.OnClearLog(sender, e);
    }

    private MainWindow? ResolveHost()
    {
        return Window.GetWindow(this) as MainWindow;
    }
}
