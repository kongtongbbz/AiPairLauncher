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

    [Fact(DisplayName = "test_session_monitor_clears_stale_approval_preview_after_resume")]
    public async Task SessionMonitorClearsStaleApprovalPreviewAfterResumeAsync()
    {
        var fakeWezTerm = new FakeWezTermService();
        var service = new SessionMonitorService(fakeWezTerm, new FakeNotificationService());
        var record = CreateRecord();
        record.StatusSnapshot.ClaudePreview = "待审批计划\n旧摘要";

        var refreshed = await service.RefreshAsync(
            [record],
            _ => new AutomationRunState
            {
                Phase = AutomationPhase.Phase3Execution,
                Status = AutomationStageStatus.WaitingForCodexReport,
                StatusDetail = "等待 Codex 执行",
            });

        Assert.Single(refreshed);
        Assert.Equal("暂无输出", refreshed[0].StatusSnapshot.ClaudePreview);
    }

    [Fact(DisplayName = "test_session_monitor_applies_timeout_backoff_and_defers_probe")]
    public async Task SessionMonitorAppliesTimeoutBackoffAndDefersProbeAsync()
    {
        var wezTerm = new CountingWezTermService(new TimeoutException("pane timeout"));
        var service = new SessionMonitorService(wezTerm, new FakeNotificationService());
        var record = CreateRecord();

        var first = await service.RefreshAsync([record], _ => null);
        Assert.Single(first);
        Assert.Equal(1, wezTerm.ProbeCount);
        Assert.Equal(SessionDisconnectReason.Timeout, first[0].StatusSnapshot.DisconnectReason);
        Assert.Equal(8, first[0].StatusSnapshot.CurrentBackoffSeconds);

        var second = await service.RefreshAsync([first[0]], _ => null);
        Assert.Single(second);
        Assert.Equal(2, wezTerm.ProbeCount);
        Assert.Equal(16, second[0].StatusSnapshot.CurrentBackoffSeconds);

        var third = await service.RefreshAsync([second[0]], _ => null);
        Assert.Single(third);
        Assert.Equal(2, wezTerm.ProbeCount);
        Assert.Contains("降低检测频率", third[0].HealthDetail);
        Assert.Equal(SessionDisconnectReason.Timeout, third[0].StatusSnapshot.DisconnectReason);
    }

    [Fact(DisplayName = "test_session_monitor_marks_process_exit_before_probe")]
    public async Task SessionMonitorMarksProcessExitBeforeProbeAsync()
    {
        var wezTerm = new CountingWezTermService();
        var service = new SessionMonitorService(wezTerm, new FakeNotificationService());
        var record = CreateRecord();
        record.Session = new LauncherSession
        {
            SessionId = record.Session.SessionId,
            Workspace = record.Session.Workspace,
            WorkingDirectory = record.Session.WorkingDirectory,
            WezTermPath = record.Session.WezTermPath,
            SocketPath = record.Session.SocketPath,
            GuiPid = int.MaxValue,
            LeftPaneId = record.Session.LeftPaneId,
            RightPaneId = record.Session.RightPaneId,
            RightPanePercent = record.Session.RightPanePercent,
            ClaudeObserverPaneId = record.Session.ClaudeObserverPaneId,
            CodexObserverPaneId = record.Session.CodexObserverPaneId,
            AutomationObserverEnabled = record.Session.AutomationObserverEnabled,
            ClaudePermissionMode = record.Session.ClaudePermissionMode,
            CodexMode = record.Session.CodexMode,
            AutomationEnabledAtLaunch = record.Session.AutomationEnabledAtLaunch,
            CreatedAt = record.Session.CreatedAt,
        };

        var refreshed = await service.RefreshAsync([record], _ => null);

        Assert.Single(refreshed);
        Assert.Equal(0, wezTerm.ProbeCount);
        Assert.Equal(SessionHealthStatus.Detached, refreshed[0].HealthStatus);
        Assert.Equal(SessionDisconnectReason.ProcessExited, refreshed[0].StatusSnapshot.DisconnectReason);
        Assert.Contains("重启终端", refreshed[0].StatusSnapshot.RecoveryHint);
    }

    [Fact(DisplayName = "test_session_monitor_marks_zombie_session_after_24h_detach")]
    public async Task SessionMonitorMarksZombieSessionAfter24hDetachAsync()
    {
        var wezTerm = new CountingWezTermService(new InvalidOperationException("socket lost"));
        var service = new SessionMonitorService(wezTerm, new FakeNotificationService());
        var record = CreateRecord();
        record.StatusSnapshot.DisconnectedAt = DateTimeOffset.Now.AddHours(-25);

        var refreshed = await service.RefreshAsync([record], _ => null);

        Assert.Single(refreshed);
        Assert.Equal(SessionHealthStatus.Detached, refreshed[0].HealthStatus);
        Assert.True(refreshed[0].StatusSnapshot.ZombieDetected);
        Assert.Contains("24小时", refreshed[0].LastSummary);
        Assert.Contains("归档", refreshed[0].StatusSnapshot.RecoveryHint);
    }

    private static ManagedSessionRecord CreateRecord()
    {
        var source = AutomationTestHelpers.CreateSession();
        var session = new LauncherSession
        {
            SessionId = source.SessionId,
            Workspace = source.Workspace,
            WorkingDirectory = source.WorkingDirectory,
            WezTermPath = source.WezTermPath,
            SocketPath = source.SocketPath,
            GuiPid = Environment.ProcessId,
            LeftPaneId = source.LeftPaneId,
            RightPaneId = source.RightPaneId,
            RightPanePercent = source.RightPanePercent,
            ClaudeObserverPaneId = source.ClaudeObserverPaneId,
            CodexObserverPaneId = source.CodexObserverPaneId,
            AutomationObserverEnabled = source.AutomationObserverEnabled,
            ClaudePermissionMode = source.ClaudePermissionMode,
            CodexMode = source.CodexMode,
            AutomationEnabledAtLaunch = source.AutomationEnabledAtLaunch,
            CreatedAt = source.CreatedAt,
        };
        return new ManagedSessionRecord
        {
            Session = session,
            DisplayName = session.Workspace,
            RuntimeBinding = new SessionRuntimeBinding
            {
                SessionId = session.SessionId,
                GuiPid = Environment.ProcessId,
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

        public event EventHandler<string?>? Activated
        {
            add { }
            remove { }
        }

        public void Notify(string title, string message, string? activationContext = null)
        {
            Messages.Add((title, message));
        }

        public void Dispose()
        {
        }
    }

    private sealed class CountingWezTermService : IWezTermService
    {
        private readonly Exception? _probeException;

        public CountingWezTermService(Exception? probeException = null)
        {
            _probeException = probeException;
        }

        public int ProbeCount { get; private set; }

        public Task<LauncherSession> StartAiPairAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ManagedWorkspaceInfo>> ListManagedWorkspacesAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<SessionReconnectResult> TryReconnectSessionAsync(ManagedSessionRecord sessionRecord, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<PaneInfo>> GetWorkspacePanesAsync(LauncherSession session, string workspace, CancellationToken cancellationToken = default)
        {
            ProbeCount += 1;
            if (_probeException is not null)
            {
                throw _probeException;
            }

            IReadOnlyList<PaneInfo> panes =
            [
                new PaneInfo { PaneId = session.LeftPaneId, Workspace = workspace, LeftCol = 0, Rows = 40, Cols = 100 },
                new PaneInfo { PaneId = session.RightPaneId, Workspace = workspace, LeftCol = 100, Rows = 40, Cols = 100 },
            ];
            return Task.FromResult(panes);
        }

        public Task FocusPaneAsync(LauncherSession session, int paneId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<WorktreeLaunchContext> CreateWorktreeLaunchContextAsync(LaunchRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<WorktreeMaintenanceResult> CleanupWorktreeAsync(string workingDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<string>> CleanupOrphanedWorktreesAsync(string workingDirectory, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> ReadPaneTextAsync(LauncherSession session, int paneId, int lastLines, CancellationToken cancellationToken = default)
        {
            if (_probeException is not null)
            {
                throw _probeException;
            }

            return Task.FromResult(string.Empty);
        }

        public Task SendTextToPaneAsync(LauncherSession session, int paneId, string text, bool submit, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SendAutomationPromptAsync(LauncherSession session, AgentRole role, string prompt, bool submit, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ContextTransferResult> SendContextAsync(LauncherSession session, SendContextRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
