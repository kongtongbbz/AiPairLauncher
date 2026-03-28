namespace AiPairLauncher.App.Models;

public sealed class TaskMdSnapshot
{
    public string? FilePath { get; init; }

    public TaskMdStatus Status { get; init; } = TaskMdStatus.Unknown;

    public int StageCount { get; init; }

    public int TaskCount { get; init; }

    public int CompletedTaskCount { get; init; }

    public string CurrentStageHeading { get; init; } = "暂无";

    public string CurrentTaskRef { get; init; } = "暂无";

    public string CurrentTaskRole { get; init; } = "暂无";

    public string CurrentTaskDependencies { get; init; } = "暂无";

    public string CurrentTaskRisk { get; init; } = "暂无";

    public string CurrentTaskExecutionSummary { get; init; } = "暂无";

    public int ReviewSectionCount { get; init; }

    public string ReviewSummary { get; init; } = "暂无";

    public IReadOnlyList<string> Errors { get; init; } = [];
}
