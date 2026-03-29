namespace AiPairLauncher.App.Models;

public sealed class TaskMdRevisionRecord
{
    public string RevisionId { get; init; } = Guid.NewGuid().ToString("N");

    public string SessionId { get; init; } = string.Empty;

    public string? TaskMdPath { get; init; }

    public TaskMdStatus TaskMdStatus { get; init; } = TaskMdStatus.Unknown;

    public AutomationPhase Phase { get; init; } = AutomationPhase.None;

    public int? StageId { get; init; }

    public string? TaskRef { get; init; }

    public AgentRole? ActiveExecutor { get; init; }

    public AutomationParallelismPolicy? ParallelismPolicy { get; init; }

    public int? MaxParallelSubagents { get; init; }

    public string ContentHash { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.Now;
}
