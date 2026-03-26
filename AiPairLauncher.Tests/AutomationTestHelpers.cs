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
        return new LauncherSession
        {
            Workspace = "test-workspace",
            WorkingDirectory = "D:\\Temp",
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
        return new LauncherSession
        {
            Workspace = "manual-workspace",
            WorkingDirectory = "D:\\Temp",
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

    public static string BuildStagePlanPacket(int stageId, string title = "阶段计划", string codexBrief = "执行任务")
    {
        return $"""
[AIPAIR_PACKET]
role: claude
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
codex_brief: <<<CODEX
{codexBrief}
CODEX
[/AIPAIR_PACKET]
""";
    }

    public static string BuildExecutionReportPacket(int stageId, string summary = "已执行")
    {
        return $"""
[AIPAIR_PACKET]
role: codex
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

    public static string BuildReviewDecisionPacket(int stageId, string decision, string codexBrief = "继续执行")
    {
        return $"""
[AIPAIR_PACKET]
role: claude
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
codex_brief: <<<CODEX
{codexBrief}
CODEX
body: <<<BODY
审定说明
BODY
[/AIPAIR_PACKET]
""";
    }
}
