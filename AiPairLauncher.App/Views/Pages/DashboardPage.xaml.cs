using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AiPairLauncher.App.ViewModels.Pages;

namespace AiPairLauncher.App.Views.Pages;

public partial class DashboardPage : System.Windows.Controls.UserControl
{
    public DashboardPage()
    {
        InitializeComponent();
    }

    private void RefreshSessions_Click(object sender, RoutedEventArgs e) => ResolveHost()?.RefreshSessions_Click(sender, e);

    private void RefreshDependencies_Click(object sender, RoutedEventArgs e) => ResolveHost()?.RefreshDependencies_Click(sender, e);

    private void ApplyLaunchProfile_Click(object sender, RoutedEventArgs e) => ResolveHost()?.ApplyLaunchProfile_Click(sender, e);

    private void StartSession_Click(object sender, RoutedEventArgs e) => ResolveHost()?.StartSession_Click(sender, e);

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e) => ResolveHost()?.BrowseWorkingDirectory_Click(sender, e);

    private void SelectPendingSession_Click(object sender, RoutedEventArgs e) => ResolveHost()?.SelectPendingSession_Click(sender, e);

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ResolveHost()?.SessionList_SelectionChanged(sender, e);

    private void TogglePinSession_Click(object sender, RoutedEventArgs e) => ResolveHost()?.TogglePinSession_Click(sender, e);

    private void CopySessionConfig_Click(object sender, RoutedEventArgs e) => ResolveHost()?.CopySessionConfig_Click(sender, e);

    private void ArchiveSession_Click(object sender, RoutedEventArgs e) => ResolveHost()?.ArchiveSession_Click(sender, e);

    private void RestoreSession_Click(object sender, RoutedEventArgs e) => ResolveHost()?.RestoreSession_Click(sender, e);

    private void OpenWorktreePath_Click(object sender, RoutedEventArgs e) => ResolveHost()?.OpenWorktreePath_Click(sender, e);

    private void SessionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        NavigateToCurrentSessionDetail();
    }

    private void OpenSessionDetail_Click(object sender, RoutedEventArgs e)
    {
        var sessionId = sender is FrameworkElement element ? element.Tag as string : null;
        if (!string.IsNullOrWhiteSpace(sessionId) && DataContext is DashboardPageViewModel viewModel)
        {
            viewModel.Core.SelectSessionById(sessionId);
        }

        NavigateToCurrentSessionDetail();
    }

    private void NavigateToCurrentSessionDetail()
    {
        if (DataContext is not DashboardPageViewModel viewModel)
        {
            return;
        }

        var sessionId = viewModel.Core.SelectedSessionRecord?.SessionId;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            ResolveHost()?.NavigateToSessionDetail(sessionId);
        }
    }

    private MainWindow? ResolveHost()
    {
        return Window.GetWindow(this) as MainWindow;
    }
}
