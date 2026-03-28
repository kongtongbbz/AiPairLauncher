using System.Windows;
using System.Windows.Controls;

namespace AiPairLauncher.App.Views.Pages;

public partial class ContextBridgePage : System.Windows.Controls.UserControl
{
    public ContextBridgePage()
    {
        InitializeComponent();
    }

    private void SendLeftToRight_Click(object sender, RoutedEventArgs e) => ResolveHost()?.SendLeftToRight_Click(sender, e);

    private void SendRightToLeft_Click(object sender, RoutedEventArgs e) => ResolveHost()?.SendRightToLeft_Click(sender, e);

    private MainWindow? ResolveHost()
    {
        return Window.GetWindow(this) as MainWindow;
    }
}
