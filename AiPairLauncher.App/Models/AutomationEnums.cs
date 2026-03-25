namespace AiPairLauncher.App.Models;

public enum AgentRole
{
    Claude,
    Codex,
}

public enum PacketKind
{
    StagePlan,
    ExecutionReport,
    ReviewDecision,
}

public enum ReviewDecision
{
    NextStage,
    RetryStage,
    Complete,
    Blocked,
}

public enum AutomationAdvancePolicy
{
    ManualEachStage,
    ManualFirstStageThenAuto,
    FullAutoLoop,
}

public enum AutomationStageStatus
{
    Idle,
    BootstrappingClaude,
    WaitingForClaudePlan,
    PendingUserApproval,
    WaitingForCodexReport,
    WaitingForClaudeReview,
    Completed,
    PausedOnError,
    Stopped,
}

public enum PacketParseStatus
{
    NoPacket,
    Success,
    Invalid,
}
