using System.IO;
using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;

namespace AiPairLauncher.Tests;

internal sealed class FakeWezTermService : IWezTermService
{
    private readonly Dictionary<int, string> _paneTexts = [];

    public List<(int PaneId, string Text, bool Submit)> SentMessages { get; } = [];
    public List<(AgentRole Role, string Prompt, bool Submit)> SentAutomationPrompts { get; } = [];
    public List<int> FocusedPaneIds { get; } = [];

    public Exception? ReadException { get; set; }

    public Exception? PaneException { get; set; }

    public IReadOnlyList<PaneInfo>? PaneOverride { get; set; }

    public IReadOnlyList<ManagedWorkspaceInfo> ManagedWorkspaces { get; set; } = [];

    public SessionReconnectResult? ReconnectResultOverride { get; set; }

    public WorktreeLaunchContext? WorktreeLaunchContextOverride { get; set; }

    public WorktreeMaintenanceResult? WorktreeMaintenanceResultOverride { get; set; }

    public IReadOnlyList<string> CleanupOrphanedWorktreesResult { get; set; } = [];

    public LaunchRequest? LastLaunchRequest { get; private set; }

    public void SetPaneText(int paneId, string text)
    {
        _paneTexts[paneId] = text;
    }

    public Task<LauncherSession> StartAiPairAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        LastLaunchRequest = request;
        return Task.FromResult(new LauncherSession
        {
            Workspace = request.Workspace ?? "generated-workspace",
            WorkingDirectory = request.ResolvedWorkingDirectory ?? request.WorkingDirectory,
            WezTermPath = "wezterm.exe",
            SocketPath = "sock",
            GuiPid = 999,
            LeftPaneId = 11,
            RightPaneId = 12,
            RightPanePercent = request.RightPanePercent,
            AutomationObserverEnabled = request.AutomationObserverEnabled,
            ClaudePermissionMode = request.ClaudePermissionMode,
            CodexMode = request.CodexMode,
            AutomationEnabledAtLaunch = request.AutomationEnabled,
        });
    }

    public Task<IReadOnlyList<ManagedWorkspaceInfo>> ListManagedWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ManagedWorkspaces);
    }

    public Task<SessionReconnectResult> TryReconnectSessionAsync(ManagedSessionRecord sessionRecord, CancellationToken cancellationToken = default)
    {
        if (ReconnectResultOverride is not null)
        {
            return Task.FromResult(ReconnectResultOverride);
        }

        return Task.FromResult(new SessionReconnectResult
        {
            Success = false,
            FailureReason = "not configured",
        });
    }

    public Task<IReadOnlyList<PaneInfo>> GetWorkspacePanesAsync(LauncherSession session, string workspace, CancellationToken cancellationToken = default)
    {
        if (PaneException is not null)
        {
            throw PaneException;
        }

        if (PaneOverride is not null)
        {
            return Task.FromResult(PaneOverride);
        }

        IReadOnlyList<PaneInfo> panes =
        [
            new PaneInfo { PaneId = session.LeftPaneId, Workspace = workspace, LeftCol = 0, Cols = 100, Rows = 40 },
            new PaneInfo { PaneId = session.RightPaneId, Workspace = workspace, LeftCol = 100, Cols = 100, Rows = 40 },
        ];

        return Task.FromResult(panes);
    }

    public Task FocusPaneAsync(LauncherSession session, int paneId, CancellationToken cancellationToken = default)
    {
        FocusedPaneIds.Add(paneId);
        return Task.CompletedTask;
    }

    public Task<WorktreeLaunchContext> CreateWorktreeLaunchContextAsync(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        if (WorktreeLaunchContextOverride is not null)
        {
            return Task.FromResult(WorktreeLaunchContextOverride);
        }

        return Task.FromResult(new WorktreeLaunchContext
        {
            WorkingDirectory = request.WorkingDirectory,
            UsedWorktree = false,
            WorktreeStrategy = request.WorktreeStrategy,
            Summary = "未启用 worktree，继续使用原目录启动。",
        });
    }

    public Task<WorktreeMaintenanceResult> CleanupWorktreeAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        if (WorktreeMaintenanceResultOverride is not null)
        {
            return Task.FromResult(WorktreeMaintenanceResultOverride);
        }

        return Task.FromResult(new WorktreeMaintenanceResult
        {
            Success = true,
            Summary = "worktree 已清理",
            WorkingDirectory = workingDirectory,
            WorktreeRemoved = true,
        });
    }

    public Task<IReadOnlyList<string>> CleanupOrphanedWorktreesAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CleanupOrphanedWorktreesResult);
    }

    public Task<string> ReadPaneTextAsync(LauncherSession session, int paneId, int lastLines, CancellationToken cancellationToken = default)
    {
        if (ReadException is not null)
        {
            throw ReadException;
        }

        return Task.FromResult(_paneTexts.TryGetValue(paneId, out var text) ? text : string.Empty);
    }

    public Task SendTextToPaneAsync(LauncherSession session, int paneId, string text, bool submit, CancellationToken cancellationToken = default)
    {
        SentMessages.Add((paneId, text, submit));
        return Task.CompletedTask;
    }

    public Task SendAutomationPromptAsync(
        LauncherSession session,
        AgentRole role,
        string prompt,
        bool submit,
        CancellationToken cancellationToken = default)
    {
        SentAutomationPrompts.Add((role, prompt, submit));
        return Task.CompletedTask;
    }

    public Task<ContextTransferResult> SendContextAsync(LauncherSession session, SendContextRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

internal static class AutomationTestHelpers
{
    public static LauncherSession CreateSession()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "AiPairLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        WriteDefaultTaskMd(workingDirectory, TaskMdStatus.PendingPlan);
        return new LauncherSession
        {
            Workspace = "test-workspace",
            WorkingDirectory = workingDirectory,
            WezTermPath = "wezterm.exe",
            SocketPath = "sock",
            GuiPid = 100,
            LeftPaneId = 1,
            RightPaneId = 2,
            RightPanePercent = 60,
            ClaudeObserverPaneId = 3,
            CodexObserverPaneId = 4,
            AutomationObserverEnabled = true,
            ClaudePermissionMode = "plan",
            CodexMode = "full-auto",
            AutomationEnabledAtLaunch = true,
        };
    }

    public static LauncherSession CreateManualSession()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "AiPairLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        WriteDefaultTaskMd(workingDirectory, TaskMdStatus.PendingPlan);
        return new LauncherSession
        {
            Workspace = "manual-workspace",
            WorkingDirectory = workingDirectory,
            WezTermPath = "wezterm.exe",
            SocketPath = "sock",
            GuiPid = 100,
            LeftPaneId = 1,
            RightPaneId = 2,
            RightPanePercent = 60,
            AutomationObserverEnabled = false,
            ClaudePermissionMode = "default",
            CodexMode = "standard",
            AutomationEnabledAtLaunch = false,
        };
    }

    public static AutomationSettings CreateSettings(
        int timeoutSeconds = 2,
        AutomationAdvancePolicy advancePolicy = AutomationAdvancePolicy.FullAutoLoop,
        int maxAutoStages = 8,
        int maxRetryPerStage = 2)
    {
        return new AutomationSettings
        {
            InitialTaskPrompt = "请实现自动编排并完成验证。",
            AdvancePolicy = advancePolicy,
            PollIntervalMilliseconds = 200,
            CaptureLines = 220,
            SubmitOnSend = true,
            NoProgressTimeoutSeconds = timeoutSeconds,
            MaxAutoStages = maxAutoStages,
            MaxRetryPerStage = maxRetryPerStage,
        };
    }

    public static AutomationSettings CreateSettingsWithExecutors(
        AgentRole phase1Executor,
        AgentRole phase2Executor,
        AgentRole phase3Executor,
        AgentRole phase4Executor,
        string? parallelismPolicy = null,
        int? maxParallelSubagents = null,
        int timeoutSeconds = 2,
        AutomationAdvancePolicy advancePolicy = AutomationAdvancePolicy.FullAutoLoop,
        int maxAutoStages = 8,
        int maxRetryPerStage = 2)
    {
        var settings = CreateSettings(timeoutSeconds, advancePolicy, maxAutoStages, maxRetryPerStage);
        TestReflection.SetProperty(settings, "Phase1Executor", phase1Executor);
        TestReflection.SetProperty(settings, "Phase2Executor", phase2Executor);
        TestReflection.SetProperty(settings, "Phase3Executor", phase3Executor);
        TestReflection.SetProperty(settings, "Phase4Executor", phase4Executor);
        if (!string.IsNullOrWhiteSpace(parallelismPolicy))
        {
            TestReflection.SetProperty(settings, "ParallelismPolicy", parallelismPolicy);
        }

        if (maxParallelSubagents.HasValue)
        {
            TestReflection.SetProperty(settings, "MaxParallelSubagents", maxParallelSubagents.Value);
        }

        return settings;
    }

    public static async Task<bool> WaitForConditionAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        return condition();
    }

    public static string BuildStagePlanPacket(
        int stageId,
        string title = "阶段计划",
        string codexBrief = "执行任务",
        AgentRole role = AgentRole.Claude,
        string briefKey = "codex_brief")
    {
        var roleValue = role == AgentRole.Codex ? "codex" : "claude";
        var normalizedBriefKey = string.IsNullOrWhiteSpace(briefKey) ? "codex_brief" : briefKey;
        return $"""
[AIPAIR_PACKET]
role: {roleValue}
kind: stage_plan
stage_id: {stageId}
title: {title}
summary: <<<SUMMARY
阶段 {stageId} 摘要
SUMMARY
scope: <<<SCOPE
仅修改必要代码
SCOPE
steps: <<<STEPS
1. 完成任务
2. 运行验证
STEPS
acceptance: <<<ACCEPTANCE
1. 构建通过
2. 输出正确
ACCEPTANCE
{normalizedBriefKey}: <<<CODEX
{codexBrief}
CODEX
[/AIPAIR_PACKET]
""";
    }

    public static string BuildExecutionReportPacket(
        int stageId,
        string summary = "已执行",
        AgentRole role = AgentRole.Codex)
    {
        var roleValue = role == AgentRole.Claude ? "claude" : "codex";
        return $"""
[AIPAIR_PACKET]
role: {roleValue}
kind: execution_report
stage_id: {stageId}
status: success
summary: <<<SUMMARY
{summary}
SUMMARY
completed: <<<COMPLETED
1. 完成编码
COMPLETED
verification: <<<VERIFICATION
1. 运行构建
VERIFICATION
blockers: <<<BLOCKERS
无
BLOCKERS
review_focus: <<<FOCUS
请检查边界条件
FOCUS
body: <<<BODY
执行完成，请审定。
BODY
[/AIPAIR_PACKET]
""";
    }

    public static string BuildReviewDecisionPacket(
        int stageId,
        string decision,
        string codexBrief = "继续执行",
        AgentRole? role = null,
        string briefKey = "codex_brief")
    {
        var resolvedRole = role ?? AgentRole.Claude;
        var roleValue = resolvedRole == AgentRole.Codex ? "codex" : "claude";
        var normalizedBriefKey = string.IsNullOrWhiteSpace(briefKey) ? "codex_brief" : briefKey;
        return $"""
[AIPAIR_PACKET]
role: {roleValue}
kind: review_decision
stage_id: {stageId}
decision: {decision}
title: 阶段 {stageId} 后续
summary: <<<SUMMARY
审定通过，给出后续动作。
SUMMARY
steps: <<<STEPS
1. 下一步处理
STEPS
acceptance: <<<ACCEPTANCE
1. 下一阶段验收
ACCEPTANCE
{normalizedBriefKey}: <<<CODEX
{codexBrief}
CODEX
body: <<<BODY
审定说明
BODY
[/AIPAIR_PACKET]
""";
    }

    public static string BuildPhaseStagePlanPacket(
        AutomationPhase phase,
        int stageId,
        string taskMdPath,
        TaskMdStatus taskMdStatus,
        string title = "阶段计划",
        string codexBrief = "执行任务",
        AgentRole? role = null,
        string briefKey = "codex_brief")
    {
        var resolvedRole = role ?? (phase == AutomationPhase.Phase3Execution ? AgentRole.Codex : AgentRole.Claude);
        WriteTaskMdFile(taskMdPath, taskMdStatus);
        var roleValue = resolvedRole == AgentRole.Codex ? "codex" : "claude";
        var normalizedBriefKey = string.IsNullOrWhiteSpace(briefKey) ? "codex_brief" : briefKey;
        return $"""
[AIPAIR_PACKET]
role: {roleValue}
kind: stage_plan
phase: {ToPhaseValue(phase)}
subagent: planner
stage_id: {stageId}
task_md_path: {taskMdPath}
task_md_status: {ToTaskMdStatusValue(taskMdStatus)}
title: {title}
summary: <<<SUMMARY
阶段 {stageId} 摘要
SUMMARY
scope: <<<SCOPE
仅修改必要代码
SCOPE
steps: <<<STEPS
1. 完成任务
2. 运行验证
STEPS
acceptance: <<<ACCEPTANCE
1. 构建通过
2. 输出正确
ACCEPTANCE
{normalizedBriefKey}: <<<CODEX_BRIEF
{codexBrief}
CODEX_BRIEF
[/AIPAIR_PACKET]
""";
    }

    public static string BuildPhaseExecutionReportPacket(
        int stageId,
        string taskMdPath,
        TaskMdStatus taskMdStatus,
        string summary = "已执行",
        string? taskRef = null,
        AgentRole role = AgentRole.Codex)
    {
        WriteTaskMdFile(taskMdPath, taskMdStatus);
        var roleValue = role == AgentRole.Claude ? "claude" : "codex";
        var taskRefLine = string.IsNullOrWhiteSpace(taskRef) ? string.Empty : $"task_ref: {taskRef}{Environment.NewLine}";
        return $"""
[AIPAIR_PACKET]
role: {roleValue}
kind: execution_report
phase: phase3_execution
stage_id: {stageId}
{taskRefLine}task_md_path: {taskMdPath}
task_md_status: {ToTaskMdStatusValue(taskMdStatus)}
status: success
summary: <<<SUMMARY
{summary}
SUMMARY
completed: <<<COMPLETED
1. 完成编码
COMPLETED
verification: <<<VERIFICATION
1. 运行构建
VERIFICATION
blockers: <<<BLOCKERS
无
BLOCKERS
review_focus: <<<FOCUS
请检查边界条件
FOCUS
body: <<<BODY
执行完成，请审定。
BODY
[/AIPAIR_PACKET]
""";
    }

    public static string BuildPhaseReviewDecisionPacket(
        AutomationPhase phase,
        int stageId,
        string decision,
        string taskMdPath,
        TaskMdStatus taskMdStatus,
        string codexBrief = "继续执行",
        string? taskRef = null,
        AgentRole? role = null,
        string briefKey = "codex_brief")
    {
        var resolvedRole = role ?? (phase == AutomationPhase.Phase4Review ? AgentRole.Claude : AgentRole.Codex);
        WriteTaskMdFile(taskMdPath, taskMdStatus);
        var roleValue = resolvedRole == AgentRole.Codex ? "codex" : "claude";
        var normalizedBriefKey = string.IsNullOrWhiteSpace(briefKey) ? "codex_brief" : briefKey;
        var taskRefLine = string.IsNullOrWhiteSpace(taskRef) ? string.Empty : $"task_ref: {taskRef}{Environment.NewLine}";
        var codexSection = decision is "next_stage" or "retry_stage"
            ? $"""
title: 阶段 {stageId} 后续
summary: <<<SUMMARY
审定通过，给出后续动作。
SUMMARY
steps: <<<STEPS
1. 下一步处理
STEPS
acceptance: <<<ACCEPTANCE
1. 下一阶段验收
ACCEPTANCE
{normalizedBriefKey}: <<<CODEX
{codexBrief}
CODEX
"""
            : string.Empty;

        return $"""
[AIPAIR_PACKET]
role: {roleValue}
kind: review_decision
phase: {ToPhaseValue(phase)}
stage_id: {stageId}
{taskRefLine}task_md_path: {taskMdPath}
task_md_status: {ToTaskMdStatusValue(taskMdStatus)}
decision: {decision}
{codexSection}body: <<<BODY
审定说明
BODY
[/AIPAIR_PACKET]
""";
    }

    public static string WriteTaskMd(LauncherSession session, TaskMdStatus status, string body)
    {
        var taskMdPath = TaskMdPathResolver.BuildDefault(session.WorkingDirectory);
        var taskMdDirectory = Path.GetDirectoryName(taskMdPath);
        if (!string.IsNullOrWhiteSpace(taskMdDirectory))
        {
            Directory.CreateDirectory(taskMdDirectory);
        }

        File.WriteAllText(
            taskMdPath,
            $$"""
# Task: 自动编排测试

> 生成时间: 2026-03-28T10:30:00+08:00
> 工作目录: {{session.WorkingDirectory}}
> 状态: {{ToTaskMdStatusHeader(status)}}

## 任务清单

{{body}}
""");
        return taskMdPath;
    }

    private static void WriteDefaultTaskMd(string workingDirectory, TaskMdStatus status)
    {
        var session = new LauncherSession
        {
            Workspace = "bootstrap",
            WorkingDirectory = workingDirectory,
            WezTermPath = "wezterm.exe",
            SocketPath = "sock",
            GuiPid = 0,
            LeftPaneId = 0,
            RightPaneId = 0,
            RightPanePercent = 60,
            AutomationEnabledAtLaunch = true,
        };

        WriteTaskMd(session, status, "### 阶段 1: 启动\n\n- [ ] **T1.1**: 初始化");
    }

    private static void WriteTaskMdFile(string taskMdPath, TaskMdStatus status)
    {
        var directory = Path.GetDirectoryName(taskMdPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            taskMdPath,
            $$"""
# Task: 自动编排测试

> 生成时间: 2026-03-28T10:30:00+08:00
> 工作目录: {{directory ?? string.Empty}}
> 状态: {{ToTaskMdStatusHeader(status)}}

## 任务清单

### 阶段 1: 启动

- [ ] **T1.1**: 初始化
""");
    }

    private static string ToPhaseValue(AutomationPhase phase)
    {
        return phase switch
        {
            AutomationPhase.Phase1Research => "phase1_research",
            AutomationPhase.Phase2Planning => "phase2_planning",
            AutomationPhase.Phase3Execution => "phase3_execution",
            AutomationPhase.Phase4Review => "phase4_review",
            _ => "phase3_execution",
        };
    }

    private static string ToTaskMdStatusValue(TaskMdStatus status)
    {
        return status switch
        {
            TaskMdStatus.PendingPlan => "pending_plan",
            TaskMdStatus.Planned => "planned",
            TaskMdStatus.InProgress => "in_progress",
            TaskMdStatus.Done => "done",
            _ => "unknown",
        };
    }

    private static string ToTaskMdStatusHeader(TaskMdStatus status)
    {
        return status switch
        {
            TaskMdStatus.PendingPlan => "PENDING_PLAN",
            TaskMdStatus.Planned => "PLANNED",
            TaskMdStatus.InProgress => "IN_PROGRESS",
            TaskMdStatus.Done => "DONE",
            _ => "UNKNOWN",
        };
    }
}

internal static class TestReflection
{
    public static void SetProperty(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property is null)
        {
            throw new InvalidOperationException($"缺少属性 {propertyName}，请先补齐实现。");
        }

        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object? convertedValue = value;
        if (propertyType.IsEnum)
        {
            if (value is string text)
            {
                convertedValue = Enum.Parse(propertyType, text, ignoreCase: true);
            }
            else if (value.GetType() != propertyType)
            {
                convertedValue = Enum.Parse(propertyType, value.ToString() ?? string.Empty, ignoreCase: true);
            }
        }
        else if (propertyType == typeof(string))
        {
            convertedValue = value.ToString();
        }
        else if (propertyType != value.GetType() && value is IConvertible)
        {
            convertedValue = Convert.ChangeType(value, propertyType);
        }

        property.SetValue(target, convertedValue);
    }

    public static string GetPropertyString(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property is null)
        {
            throw new InvalidOperationException($"缺少属性 {propertyName}，请先补齐实现。");
        }

        var value = property.GetValue(target);
        return value?.ToString() ?? string.Empty;
    }

    public static bool GetPropertyBool(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property is null)
        {
            throw new InvalidOperationException($"缺少属性 {propertyName}，请先补齐实现。");
        }

        var value = property.GetValue(target);
        return value is bool flag && flag;
    }
}
