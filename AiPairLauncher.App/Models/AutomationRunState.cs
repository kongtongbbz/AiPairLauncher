namespace AiPairLauncher.App.Models;

public sealed class AutomationRunState
{
    public AutomationStageStatus Status { get; init; } = AutomationStageStatus.Idle;

    public int? CurrentStageId { get; init; }

    public string StatusDetail { get; init; } = "空闲";

    public string LastPacketSummary { get; init; } = "暂无";

    public ApprovalDraft? PendingApproval { get; init; }

    public string? LastError { get; init; }

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
