using System.IO;
using System.Text.RegularExpressions;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed partial class TaskMdParser
{
    [GeneratedRegex(@"^>\s*状态:\s*(?<status>[A-Z_]+)\s*$", RegexOptions.Multiline)]
    private static partial Regex StatusRegex();

    [GeneratedRegex(@"^###\s+(?<heading>.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex StageRegex();

    [GeneratedRegex(@"^##\s+任务清单\s*$", RegexOptions.Multiline)]
    private static partial Regex TaskSectionRegex();

    [GeneratedRegex(@"^##\s+.+?$", RegexOptions.Multiline)]
    private static partial Regex SectionHeadingRegex();

    [GeneratedRegex(@"^-\s+\[(?<done>[ xX])\]\s+\*\*(?<taskRef>T[\w\.\-]+)\*\*:\s*(?<title>.+?)(?<warning>\s+⚠️.*)?$", RegexOptions.Multiline)]
    private static partial Regex TaskRegex();

    public TaskMdValidationResult ParseFile(string taskMdPath)
    {
        if (string.IsNullOrWhiteSpace(taskMdPath))
        {
            return BuildError("task.md 路径不能为空。");
        }

        if (!File.Exists(taskMdPath))
        {
            return BuildError($"未找到 task.md: {taskMdPath}");
        }

        var content = File.ReadAllText(taskMdPath);
        return ParseContent(content, taskMdPath);
    }

    public TaskMdValidationResult ParseContent(string content, string? taskMdPath = null)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return BuildError("task.md 内容为空。", taskMdPath);
        }

        var statusMatch = StatusRegex().Match(content);
        var status = statusMatch.Success
            ? ParseTaskMdStatus(statusMatch.Groups["status"].Value, errors)
            : TaskMdStatus.Unknown;
        if (!statusMatch.Success)
        {
            errors.Add("task.md 缺少文件头状态行。");
        }

        var stages = ParseStages(content, errors);
        if (stages.Count == 0)
        {
            errors.Add("task.md 未解析到任何阶段。");
        }

        var document = stages.Count == 0
            ? null
            : new TaskMdDocument
            {
                FilePath = taskMdPath,
                Status = status,
                Stages = stages,
            };

        return new TaskMdValidationResult
        {
            Document = document,
            Errors = errors,
            Snapshot = BuildSnapshot(document, taskMdPath, errors),
        };
    }

    private static IReadOnlyList<TaskMdStage> ParseStages(string content, List<string> errors)
    {
        var taskSectionMatch = TaskSectionRegex().Match(content);
        if (!taskSectionMatch.Success)
        {
            errors.Add("task.md 缺少“## 任务清单”章节。");
            return [];
        }

        var taskSectionStartIndex = taskSectionMatch.Index;
        var nextSectionIndex = SectionHeadingRegex()
            .Matches(content)
            .Cast<Match>()
            .Where(match => match.Index > taskSectionStartIndex)
            .Select(match => match.Index)
            .DefaultIfEmpty(content.Length)
            .First();

        var taskSectionContent = content[taskSectionStartIndex..nextSectionIndex];
        var stageMatches = StageRegex().Matches(taskSectionContent);
        var stages = new List<TaskMdStage>();
        for (var index = 0; index < stageMatches.Count; index++)
        {
            var stageMatch = stageMatches[index];
            var nextStart = index + 1 < stageMatches.Count ? stageMatches[index + 1].Index : taskSectionContent.Length;
            var block = taskSectionContent[stageMatch.Index..nextStart];
            var heading = stageMatch.Groups["heading"].Value.Trim();
            var tasks = ParseTasks(block, heading, errors);
            stages.Add(new TaskMdStage
            {
                Heading = heading,
                Tasks = tasks,
            });
        }

        return stages;
    }

    private static IReadOnlyList<TaskMdTask> ParseTasks(string stageBlock, string stageHeading, List<string> errors)
    {
        var matches = TaskRegex().Matches(stageBlock);
        var tasks = new List<TaskMdTask>();
        foreach (Match match in matches)
        {
            var taskRef = match.Groups["taskRef"].Value.Trim();
            if (string.IsNullOrWhiteSpace(taskRef))
            {
                errors.Add($"阶段 {stageHeading} 中存在缺少任务编号的任务。");
                continue;
            }

            tasks.Add(new TaskMdTask
            {
                TaskRef = taskRef,
                Title = match.Groups["title"].Value.Trim(),
                StageHeading = stageHeading,
                IsCompleted = string.Equals(match.Groups["done"].Value, "x", StringComparison.OrdinalIgnoreCase),
                IsWarning = !string.IsNullOrWhiteSpace(match.Groups["warning"].Value),
            });
        }

        return tasks;
    }

    private static TaskMdStatus ParseTaskMdStatus(string rawStatus, List<string> errors)
    {
        return rawStatus.Trim().ToUpperInvariant() switch
        {
            "PENDING_PLAN" => TaskMdStatus.PendingPlan,
            "PLANNED" => TaskMdStatus.Planned,
            "IN_PROGRESS" => TaskMdStatus.InProgress,
            "DONE" => TaskMdStatus.Done,
            var unknown =>
                AddUnknownStatusError(errors, unknown),
        };
    }

    private static TaskMdStatus AddUnknownStatusError(List<string> errors, string rawStatus)
    {
        errors.Add($"task.md 状态无效: {rawStatus}");
        return TaskMdStatus.Unknown;
    }

    private static TaskMdSnapshot BuildSnapshot(TaskMdDocument? document, string? taskMdPath, IReadOnlyList<string> errors)
    {
        if (document is null)
        {
            return new TaskMdSnapshot
            {
                FilePath = taskMdPath,
                Errors = errors.ToArray(),
            };
        }

        var currentTask = document.Stages
            .SelectMany(static stage => stage.Tasks)
            .FirstOrDefault(static task => !task.IsCompleted);
        var currentStage = currentTask?.StageHeading ?? document.Stages.LastOrDefault()?.Heading ?? "暂无";

        return new TaskMdSnapshot
        {
            FilePath = document.FilePath,
            Status = document.Status,
            StageCount = document.Stages.Count,
            TaskCount = document.TaskCount,
            CompletedTaskCount = document.CompletedTaskCount,
            CurrentStageHeading = currentStage,
            CurrentTaskRef = currentTask?.TaskRef ?? "暂无",
            Errors = errors.ToArray(),
        };
    }

    private static TaskMdValidationResult BuildError(string error, string? taskMdPath = null)
    {
        return new TaskMdValidationResult
        {
            Errors = [error],
            Snapshot = new TaskMdSnapshot
            {
                FilePath = taskMdPath,
                Errors = [error],
            },
        };
    }
}
