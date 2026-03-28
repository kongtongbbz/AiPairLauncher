using System.Windows;
using System.Windows.Controls;

namespace AiPairLauncher.App.Views.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void ApplyLaunchProfile_Click(object sender, RoutedEventArgs e) => ResolveHost()?.ApplyLaunchProfile_Click(sender, e);

    private void SaveLaunchProfile_Click(object sender, RoutedEventArgs e) => ResolveHost()?.SaveLaunchProfile_Click(sender, e);

    private void RefreshDependencies_Click(object sender, RoutedEventArgs e) => ResolveHost()?.RefreshDependencies_Click(sender, e);

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) => ResolveHost()?.ThemeSelector_SelectionChanged(sender, e);

    private void ClearAppCache_Click(object sender, RoutedEventArgs e) => ResolveHost()?.ClearAppCache_Click(sender, e);

    private void CleanupWorktree_Click(object sender, RoutedEventArgs e) => ResolveHost()?.CleanupWorktree_Click(sender, e);

    private void CleanupOrphanedWorktrees_Click(object sender, RoutedEventArgs e) => ResolveHost()?.CleanupOrphanedWorktrees_Click(sender, e);

    private MainWindow? ResolveHost()
    {
        return Window.GetWindow(this) as MainWindow;
    }
}
