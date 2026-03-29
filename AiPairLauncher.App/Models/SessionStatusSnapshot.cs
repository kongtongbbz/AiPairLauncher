namespace AiPairLauncher.App.Models;

public sealed class SessionStatusSnapshot
{
    public string SessionId { get; set; } = string.Empty;

    public AutomationPhase AutomationPhase { get; set; } = AutomationPhase.None;

    public SessionHealthStatus Status { get; set; } = SessionHealthStatus.Idle;

    public string StatusDetail { get; set; } = "等待检测";

    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.Now;

    public string? LastError { get; set; }

    public string LastSummary { get; set; } = "暂无";

    public string ClaudePreview { get; set; } = "暂无输出";

    public string CodexPreview { get; set; } = "暂无输出";

    public bool NeedsApproval { get; set; }

    public int? AutomationStageId { get; set; }

    public string? AutomationTaskRef { get; set; }

    public string? TaskMdPath { get; set; }

    public TaskMdStatus TaskMdStatus { get; set; } = TaskMdStatus.Unknown;

    public int AutomationRetryCount { get; set; }

    public SessionDisconnectReason DisconnectReason { get; set; } = SessionDisconnectReason.None;

    public DateTimeOffset? DisconnectedAt { get; set; }

    public int CurrentBackoffSeconds { get; set; } = 8;

    public DateTimeOffset? NextHealthProbeAt { get; set; }

    public bool ZombieDetected { get; set; }

    public string RecoveryHint { get; set; } = "会话在线，无需恢复操作。";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public SessionStatusSnapshot Clone()
    {
        return new SessionStatusSnapshot
        {
            SessionId = SessionId,
            AutomationPhase = AutomationPhase,
            Status = Status,
            StatusDetail = StatusDetail,
            LastActivityAt = LastActivityAt,
            LastError = LastError,
            LastSummary = LastSummary,
            ClaudePreview = ClaudePreview,
            CodexPreview = CodexPreview,
            NeedsApproval = NeedsApproval,
            AutomationStageId = AutomationStageId,
            AutomationTaskRef = AutomationTaskRef,
            TaskMdPath = TaskMdPath,
            TaskMdStatus = TaskMdStatus,
            AutomationRetryCount = AutomationRetryCount,
            DisconnectReason = DisconnectReason,
            DisconnectedAt = DisconnectedAt,
            CurrentBackoffSeconds = CurrentBackoffSeconds,
            NextHealthProbeAt = NextHealthProbeAt,
            ZombieDetected = ZombieDetected,
            RecoveryHint = RecoveryHint,
            UpdatedAt = UpdatedAt,
        };
    }

    public static SessionStatusSnapshot CreateDefault()
    {
        return new SessionStatusSnapshot();
    }
}
