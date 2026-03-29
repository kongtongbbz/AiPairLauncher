using System.Windows;
using System.Windows.Controls;

namespace AiPairLauncher.App.Views.Pages;

public partial class AutomationPage : System.Windows.Controls.UserControl
{
    public AutomationPage()
    {
        InitializeComponent();
    }

    private void StartAutomation_Click(object sender, RoutedEventArgs e) => ResolveHost()?.StartAutomation_Click(sender, e);

    private void StopAutomation_Click(object sender, RoutedEventArgs e) => ResolveHost()?.StopAutomation_Click(sender, e);

    private void ApprovePlan_Click(object sender, RoutedEventArgs e) => ResolveHost()?.ApprovePlan_Click(sender, e);

    private void ContinueAutomationWait_Click(object sender, RoutedEventArgs e) => ResolveHost()?.ContinueAutomationWait_Click(sender, e);

    private void RetryAutomationStage_Click(object sender, RoutedEventArgs e) => ResolveHost()?.RetryAutomationStage_Click(sender, e);

    private void RejectPlan_Click(object sender, RoutedEventArgs e) => ResolveHost()?.RejectPlan_Click(sender, e);

    private MainWindow? ResolveHost()
    {
        return Window.GetWindow(this) as MainWindow;
    }
}
