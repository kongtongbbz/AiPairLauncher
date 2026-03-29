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

    [Fact(DisplayName = "test_apply_automation_state_surfaces_phase_and_taskmd_summary")]
    public void ApplyAutomationStateSurfacesPhaseAndTaskMdSummary()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.ApplyAutomationState(new AutomationRunState
        {
            Phase = AutomationPhase.Phase3Execution,
            Status = AutomationStageStatus.WaitingForCodexReport,
            StatusDetail = "等待 Codex 执行",
            TaskMdPath = @"D:\repo\task.md",
            TaskMdStatus = TaskMdStatus.InProgress,
            CurrentTaskRef = "T2.1",
            CurrentTaskStageHeading = "阶段 2: 任务执行",
            TaskCount = 6,
            CompletedTaskCount = 2,
        });

        Assert.Equal("Phase 3 · 任务执行", viewModel.AutomationPhaseLabel);
        Assert.Equal(@"D:\repo\task.md", viewModel.AutomationTaskMdPath);
        Assert.Equal("IN_PROGRESS", viewModel.AutomationTaskMdStatus);
        Assert.Equal("T2.1", viewModel.AutomationCurrentTaskRef);
        Assert.Equal("已完成 2/6 · 当前阶段 阶段 2: 任务执行", viewModel.AutomationTaskProgressSummary);
    }

    [Fact(DisplayName = "test_can_start_automation_allows_promoting_manual_session")]
    public void CanStartAutomationAllowsPromotingManualSession()
    {
        var viewModel = new MainWindowViewModel();
        var manualRecord = CreateRecord("manual", "默认", SessionHealthStatus.Idle);
        viewModel.ApplySessionCatalog([manualRecord], manualRecord.SessionId);
        viewModel.SelectedSessionRecord = viewModel.SessionRecords[0];

        Assert.True(viewModel.CanStartAutomation);
        Assert.Contains("普通双栏会话也可直接升级为自动编排会话", viewModel.AutomationStartHint);
    }

    [Fact(DisplayName = "test_can_start_automation_allows_detached_auto_session_for_reconnect_flow")]
    public void CanStartAutomationAllowsDetachedAutoSessionForReconnectFlow()
    {
        var viewModel = new MainWindowViewModel();
        var detachedAutoRecord = CreateRecord("auto-detached", "默认", SessionHealthStatus.Detached, automationEnabledAtLaunch: true);

        viewModel.ApplySessionCatalog([detachedAutoRecord], detachedAutoRecord.SessionId);
        viewModel.SelectedSessionRecord = viewModel.SessionRecords[0];
        Assert.True(viewModel.CanStartAutomation);
        Assert.Contains("点击“启动”", viewModel.AutomationStartHint);
    }

    private static ManagedSessionRecord CreateRecord(string workspace, string groupName, SessionHealthStatus status, bool automationEnabledAtLaunch = false)
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
            AutomationEnabledAtLaunch = automationEnabledAtLaunch,
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
