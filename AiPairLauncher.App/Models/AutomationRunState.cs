namespace AiPairLauncher.App.Models;

public sealed class AutomationRunState
{
    public AutomationPhase Phase { get; init; } = AutomationPhase.None;

    public AutomationStageStatus Status { get; init; } = AutomationStageStatus.Idle;

    public int? CurrentStageId { get; init; }

    public string? CurrentTaskRef { get; init; }

    public string CurrentTaskStageHeading { get; init; } = "暂无";

    public string StatusDetail { get; init; } = "空闲";

    public string LastPacketSummary { get; init; } = "暂无";

    public string? TaskMdPath { get; init; }

    public TaskMdStatus TaskMdStatus { get; init; } = TaskMdStatus.Unknown;

    public AgentRole? ActiveExecutor { get; init; }

    public AutomationParallelismPolicy? ParallelismPolicy { get; init; }

    public int? MaxParallelSubagents { get; init; }

    public int TaskCount { get; init; }

    public int CompletedTaskCount { get; init; }

    public ApprovalDraft? PendingApproval { get; init; }

    public string? LastError { get; init; }

    public bool AutoAdvanceEnabled { get; init; }

    public int AutoApprovedStageCount { get; init; }

    public int CurrentStageRetryCount { get; init; }

    public string? InterventionReason { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    public bool HasPendingApproval => PendingApproval is not null;

    public bool IsActive =>
        Status is AutomationStageStatus.BootstrappingClaude
            or AutomationStageStatus.WaitingForClaudePlan
            or AutomationStageStatus.PendingUserApproval
            or AutomationStageStatus.WaitingForCodexReport
            or AutomationStageStatus.WaitingForClaudeReview;

    public bool IsTerminal =>
        Status is AutomationStageStatus.Completed
            or AutomationStageStatus.PausedOnError
            or AutomationStageStatus.Stopped;
}
