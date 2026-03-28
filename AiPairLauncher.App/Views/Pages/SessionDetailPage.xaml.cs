using System.Windows;
using System.Windows.Controls;

namespace AiPairLauncher.App.Views.Pages;

public partial class SessionDetailPage : System.Windows.Controls.UserControl
{
    public SessionDetailPage()
    {
        InitializeComponent();
    }

    private void ReloadLastSession_Click(object sender, RoutedEventArgs e) => ResolveHost()?.ReloadLastSession_Click(sender, e);

    private void ReconnectSession_Click(object sender, RoutedEventArgs e) => ResolveHost()?.ReconnectSession_Click(sender, e);

    private void FocusClaudePane_Click(object sender, RoutedEventArgs e) => ResolveHost()?.FocusClaudePane_Click(sender, e);

    private void FocusCodexPane_Click(object sender, RoutedEventArgs e) => ResolveHost()?.FocusCodexPane_Click(sender, e);

    private void ArchiveSession_Click(object sender, RoutedEventArgs e) => ResolveHost()?.ArchiveSession_Click(sender, e);

    private void RestoreSession_Click(object sender, RoutedEventArgs e) => ResolveHost()?.RestoreSession_Click(sender, e);

    private void SaveSessionMetadata_Click(object sender, RoutedEventArgs e) => ResolveHost()?.SaveSessionMetadata_Click(sender, e);

    private void OpenWorktreePath_Click(object sender, RoutedEventArgs e) => ResolveHost()?.OpenWorktreePath_Click(sender, e);

    private void CleanupWorktree_Click(object sender, RoutedEventArgs e) => ResolveHost()?.CleanupWorktree_Click(sender, e);

    private void CleanupOrphanedWorktrees_Click(object sender, RoutedEventArgs e) => ResolveHost()?.CleanupOrphanedWorktrees_Click(sender, e);

    private MainWindow? ResolveHost()
    {
        return Window.GetWindow(this) as MainWindow;
    }
}
