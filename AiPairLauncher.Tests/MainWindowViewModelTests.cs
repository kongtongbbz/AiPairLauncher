using AiPairLauncher.App.Models;
using AiPairLauncher.App.ViewModels;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact(DisplayName = "test_apply_launch_profile_updates_launch_form_fields_only")]
    public void ApplyLaunchProfileUpdatesLaunchFormFieldsOnly()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.WorkingDirectory = "D:\\work\\original";
        viewModel.WorkspaceName = "workspace-old";
        viewModel.ReplaceLaunchProfiles(
        [
            new LaunchProfile
            {
                ProfileId = "profile-1",
                Name = "自动闭环",
                WorkingDirectory = "D:\\work\\profile",
                WorkspacePrefix = "workspace-new",
                ClaudePermissionMode = "plan",
                CodexMode = "full-auto",
                RightPanePercent = 70,
                AutomationEnabled = true,
                DefaultUseWorktree = true,
                WorktreeStrategy = "subdirectory",
                DefaultWorktreeStrategy = "subdirectory",
            }
        ]);
        viewModel.SelectedLaunchProfileId = "profile-1";

        var applied = viewModel.ApplyLaunchProfile();

        Assert.NotNull(applied);
        Assert.Equal("D:\\work\\profile", viewModel.WorkingDirectory);
        Assert.Equal("workspace-new", viewModel.WorkspaceName);
        Assert.Equal("plan", viewModel.ClaudePermissionMode);
        Assert.Equal("full-auto", viewModel.CodexMode);
        Assert.Equal(70, viewModel.RightPanePercent);
        Assert.True(viewModel.AutoModeEnabled);
        Assert.True(viewModel.UseWorktree);
        Assert.Equal("subdirectory", viewModel.WorktreeStrategy);
    }
}
