using AiPairLauncher.App.Services;
using AiPairLauncher.App.ViewModels;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class ShellViewModelTests
{
    [Fact(DisplayName = "test_shell_viewmodel_switches_pages_and_toggles_log_drawer")]
    public void ShellViewModelSwitchesPagesAndTogglesLogDrawer()
    {
        var shell = new ShellViewModel(new MainWindowViewModel(), new NavigationService());

        shell.Navigate(NavigationPageKeys.Automation);

        Assert.Equal(NavigationPageKeys.Automation, shell.CurrentPageKey);
        Assert.Same(shell.Automation, shell.CurrentPageViewModel);

        shell.ToggleLogDrawer();

        Assert.True(shell.IsLogDrawerOpen);
    }
}
