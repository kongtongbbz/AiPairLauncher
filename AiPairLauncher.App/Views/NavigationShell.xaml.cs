using System.Windows;
using System.Windows.Controls;
using AiPairLauncher.App.ViewModels;

namespace AiPairLauncher.App.Views;

public partial class NavigationShell : System.Windows.Controls.UserControl
{
    public NavigationShell()
    {
        InitializeComponent();
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel || sender is not FrameworkElement element)
        {
            return;
        }

        var pageKey = element.Tag as string;
        viewModel.Navigate(pageKey ?? string.Empty);
    }

    private void ToggleLogDrawer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
        {
            return;
        }

        viewModel.ToggleLogDrawer();
    }
}
