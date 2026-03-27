namespace AiPairLauncher.App.Models;

public sealed class AutomationEventRecord
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");

    public string SessionId { get; init; } = string.Empty;

    public AutomationPhase Phase { get; init; } = AutomationPhase.None;

    public AutomationStageStatus Status { get; init; } = AutomationStageStatus.Idle;

    public int? StageId { get; init; }

    public string? TaskRef { get; init; }

    public string? TaskMdPath { get; init; }

    public TaskMdStatus TaskMdStatus { get; init; } = TaskMdStatus.Unknown;

    public string StatusDetail { get; init; } = string.Empty;

    public string LastPacketSummary { get; init; } = "暂无";

    public string? LastError { get; init; }

    public string? InterventionReason { get; init; }

    public ApprovalDraft? PendingApproval { get; init; }

    public int AutoApprovedStageCount { get; init; }

    public int CurrentStageRetryCount { get; init; }

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.Now;

    public string DisplayStage => StageId.HasValue ? $"阶段 {StageId.Value}" : "阶段未定";

    public string SeverityLabel => Status switch
    {
        AutomationStageStatus.PausedOnError => "错误",
        AutomationStageStatus.PendingUserApproval => "待处理",
        AutomationStageStatus.Completed => "完成",
        AutomationStageStatus.Stopped => "已停止",
        _ => "运行中",
    };

    public static AutomationEventRecord FromState(string sessionId, AutomationRunState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(state);

        return new AutomationEventRecord
        {
            SessionId = sessionId,
            Phase = state.Phase,
            Status = state.Status,
            StageId = state.CurrentStageId,
            TaskRef = state.CurrentTaskRef,
            TaskMdPath = state.TaskMdPath,
            TaskMdStatus = state.TaskMdStatus,
            StatusDetail = state.StatusDetail,
            LastPacketSummary = state.LastPacketSummary,
            LastError = state.LastError,
            InterventionReason = state.InterventionReason,
            PendingApproval = state.PendingApproval,
            AutoApprovedStageCount = state.AutoApprovedStageCount,
            CurrentStageRetryCount = state.CurrentStageRetryCount,
            OccurredAt = state.UpdatedAt,
        };
    }
}
