using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class SessionMonitorServiceTests
{
    [Fact(DisplayName = "test_session_monitor_marks_waiting_when_permission_prompt_detected")]
    public async Task SessionMonitorMarksWaitingWhenPermissionPromptDetectedAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        fakeWezTerm.SetPaneText(1, """
Quick safety check:

1. Yes, I trust this folder
2. No, exit
""");
        var notificationService = new FakeNotificationService();
        var service = new SessionMonitorService(fakeWezTerm, notificationService);
        var record = CreateRecord();

        var refreshed = await service.RefreshAsync([record], _ => null);

        Assert.Single(refreshed);
        Assert.Equal(SessionHealthStatus.Waiting, refreshed[0].HealthStatus);
        Assert.Contains("Quick safety check", refreshed[0].StatusSnapshot.ClaudePreview);
        Assert.Single(notificationService.Messages);
    }

    [Fact(DisplayName = "test_session_monitor_marks_detached_when_wezterm_unavailable")]
    public async Task SessionMonitorMarksDetachedWhenWezTermUnavailableAsync()
    {
        var fakeWezTerm = new FakeWezTermService
        {
            PaneException = new InvalidOperationException("pane lost"),
        };
        var service = new SessionMonitorService(fakeWezTerm, new FakeNotificationService());
        var record = CreateRecord();

        var refreshed = await service.RefreshAsync([record], _ => null);

        Assert.Single(refreshed);
        Assert.Equal(SessionHealthStatus.Detached, refreshed[0].HealthStatus);
        Assert.Contains("pane lost", refreshed[0].LastError);
    }

    [Fact(DisplayName = "test_session_monitor_maps_automation_state_to_running")]
    public async Task SessionMonitorMapsAutomationStateToRunningAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var service = new SessionMonitorService(fakeWezTerm, new FakeNotificationService());
        var record = CreateRecord();

        var refreshed = await service.RefreshAsync(
            [record],
            _ => new AutomationRunState
            {
                Status = AutomationStageStatus.WaitingForCodexReport,
                StatusDetail = "等待 Codex 执行",
                LastPacketSummary = "阶段已发送",
            });

        Assert.Single(refreshed);
        Assert.Equal(SessionHealthStatus.Running, refreshed[0].HealthStatus);
        Assert.Equal("阶段已发送", refreshed[0].LastSummary);
    }

    [Fact(DisplayName = "test_session_monitor_persists_phase_aware_fields")]
    public async Task SessionMonitorPersistsPhaseAwareFieldsAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var service = new SessionMonitorService(fakeWezTerm, new FakeNotificationService());
        var record = CreateRecord();

        var refreshed = await service.RefreshAsync(
            [record],
            _ => new AutomationRunState
            {
                Phase = AutomationPhase.Phase2Planning,
                Status = AutomationStageStatus.PendingUserApproval,
                StatusDetail = "等待审批",
                TaskMdPath = @"D:\repo\task.md",
                TaskMdStatus = TaskMdStatus.Planned,
                CurrentTaskRef = "T1.2",
            });

        Assert.Single(refreshed);
        Assert.Equal(SessionHealthStatus.Waiting, refreshed[0].HealthStatus);
        Assert.Equal(AutomationPhase.Phase2Planning, refreshed[0].StatusSnapshot.AutomationPhase);
        Assert.Equal(@"D:\repo\task.md", refreshed[0].StatusSnapshot.TaskMdPath);
        Assert.Equal(TaskMdStatus.Planned, refreshed[0].StatusSnapshot.TaskMdStatus);
        Assert.Equal("T1.2", refreshed[0].StatusSnapshot.AutomationTaskRef);
    }

    private static ManagedSessionRecord CreateRecord()
    {
        var session = AutomationTestHelpers.CreateSession();
        return new ManagedSessionRecord
        {
            Session = session,
            DisplayName = session.Workspace,
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
                Status = SessionHealthStatus.Idle,
                StatusDetail = "等待检测",
                LastSummary = "暂无",
            },
        };
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public List<(string Title, string Message)> Messages { get; } = [];

        public void Notify(string title, string message)
        {
            Messages.Add((title, message));
        }

        public void Dispose()
        {
        }
    }
}
