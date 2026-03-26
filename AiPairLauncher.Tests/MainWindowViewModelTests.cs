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
                DefaultGroupName = "研究组",
                TransferInstructionTemplate = "请继续执行并总结。",
                DefaultPanelPreset = "automation",
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
        Assert.Equal("研究组", viewModel.SessionGroupName);
        Assert.Equal("请继续执行并总结。", viewModel.TransferInstruction);
        Assert.Equal("automation", viewModel.PanelPreset);
        Assert.Equal("plan", viewModel.ClaudePermissionMode);
        Assert.Equal("full-auto", viewModel.CodexMode);
        Assert.Equal(70, viewModel.RightPanePercent);
        Assert.True(viewModel.AutoModeEnabled);
        Assert.True(viewModel.UseWorktree);
        Assert.Equal("subdirectory", viewModel.WorktreeStrategy);
    }

    [Fact(DisplayName = "test_apply_session_catalog_filters_by_group_and_status")]
    public void ApplySessionCatalogFiltersByGroupAndStatus()
    {
        var viewModel = new MainWindowViewModel();
        var runningRecord = CreateRecord("alpha", "默认", SessionHealthStatus.Running);
        var waitingRecord = CreateRecord("beta", "研究组", SessionHealthStatus.Waiting);

        viewModel.ApplySessionCatalog([runningRecord, waitingRecord]);
        viewModel.SelectedSessionGroupFilter = "研究组";

        Assert.Single(viewModel.SessionRecords);
        Assert.Equal("beta", viewModel.SessionRecords[0].DisplayName);

        viewModel.SelectedSessionStatusFilter = "waiting";

        Assert.Single(viewModel.SessionRecords);
        Assert.Equal(SessionHealthStatus.Waiting, viewModel.SessionRecords[0].HealthStatus);
    }

    private static ManagedSessionRecord CreateRecord(string workspace, string groupName, SessionHealthStatus status)
    {
        var session = new LauncherSession
        {
            Workspace = workspace,
            WorkingDirectory = $"D:\\work\\{workspace}",
            WezTermPath = "wezterm.exe",
            SocketPath = $"sock-{workspace}",
            GuiPid = 100,
            LeftPaneId = 1,
            RightPaneId = 2,
            RightPanePercent = 60,
            ClaudePermissionMode = "default",
            CodexMode = "standard",
            AutomationEnabledAtLaunch = false,
        };

        return new ManagedSessionRecord
        {
            Session = session,
            DisplayName = workspace,
            GroupName = groupName,
            RuntimeBinding = new SessionRuntimeBinding
            {
                SessionId = session.SessionId,
                GuiPid = session.GuiPid,
                SocketPath = session.SocketPath,
                LeftPaneId = session.LeftPaneId,
                RightPaneId = session.RightPaneId,
                IsAlive = true,
            },
            StatusSnapshot = new SessionStatusSnapshot
            {
                SessionId = session.SessionId,
                Status = status,
                StatusDetail = "测试状态",
                LastSummary = "测试摘要",
                LastActivityAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now,
            },
        };
    }
}
