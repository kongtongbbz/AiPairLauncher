using System.Collections.ObjectModel;

namespace AiPairLauncher.App.Models;

public sealed class TaskMdDocument
{
    public string? FilePath { get; init; }

    public TaskMdStatus Status { get; init; } = TaskMdStatus.Unknown;

    public IReadOnlyList<TaskMdStage> Stages { get; init; } = [];

    public int TaskCount => Stages.Sum(static stage => stage.Tasks.Count);

    public int CompletedTaskCount => Stages.Sum(stage => stage.Tasks.Count(static task => task.IsCompleted));

    public TaskMdTask? FindTask(string taskRef)
    {
        if (string.IsNullOrWhiteSpace(taskRef))
        {
            return null;
        }

        return Stages
            .SelectMany(static stage => stage.Tasks)
            .FirstOrDefault(task => string.Equals(task.TaskRef, taskRef.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class TaskMdStage
{
    public string Heading { get; init; } = string.Empty;

    public IReadOnlyList<TaskMdTask> Tasks { get; init; } = [];
}

public sealed class TaskMdTask
{
    public string TaskRef { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string StageHeading { get; init; } = string.Empty;

    public bool IsCompleted { get; init; }

    public bool IsWarning { get; init; }
}
