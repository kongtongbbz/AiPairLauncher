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

public enum AutomationPhase
{
    None,
    Phase1Research,
    Phase2Planning,
    Phase3Execution,
    Phase4Review,
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

public enum AutomationParallelismPolicy
{
    Conservative,
    Balanced,
    Aggressive,
    Auto,
}

public enum AutomationInterventionKind
{
    Approval,
    Timeout,
}

public enum PacketParseStatus
{
    NoPacket,
    Success,
    Invalid,
}

public enum TaskMdStatus
{
    Unknown,
    PendingPlan,
    Planned,
    InProgress,
    Done,
}
