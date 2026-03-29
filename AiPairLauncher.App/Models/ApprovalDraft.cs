namespace AiPairLauncher.App.Models;

public sealed class ApprovalDraft
{
    public AutomationPhase Phase { get; init; } = AutomationPhase.None;

    public int StageId { get; init; }

    public string? TaskRef { get; init; }

    public string? ParallelGroup { get; init; }

    public string? Subagent { get; init; }

    public int? RetryCount { get; init; }

    public PacketKind SourceKind { get; init; }

    public ReviewDecision? ReviewDecision { get; init; }

    public string? TaskMdPath { get; init; }

    public TaskMdStatus TaskMdStatus { get; init; } = TaskMdStatus.Unknown;

    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Scope { get; init; } = string.Empty;

    public IReadOnlyList<string> Steps { get; init; } = [];

    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];

    public IReadOnlyList<string> TaskProgress { get; init; } = [];

    public string ExecutorBrief { get; init; } = string.Empty;

    public string CodexBrief => ExecutorBrief;
}
