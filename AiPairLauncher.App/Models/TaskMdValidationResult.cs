namespace AiPairLauncher.App.Models;

public sealed class TaskMdValidationResult
{
    public bool IsValid => Errors.Count == 0 && Document is not null;

    public TaskMdDocument? Document { get; init; }

    public TaskMdSnapshot Snapshot { get; init; } = new();

    public IReadOnlyList<string> Errors { get; init; } = [];
}
