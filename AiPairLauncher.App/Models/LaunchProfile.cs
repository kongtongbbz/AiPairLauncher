namespace AiPairLauncher.App.Models;

public sealed class LaunchProfile
{
    public string ProfileId { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }

    public string? WorkspacePrefix { get; init; }

    public string DefaultGroupName { get; init; } = "默认";

    public string TransferInstructionTemplate { get; init; } = "请基于下面这段终端输出继续处理。先给出结论，再给出下一步动作。";

    public string DefaultPanelPreset { get; init; } = "balanced";

    public string ClaudePermissionMode { get; init; } = "default";

    public string CodexMode { get; init; } = "standard";

    public int RightPanePercent { get; init; } = 60;

    public bool AutomationEnabled { get; init; }

    public bool AutomationObserverEnabled { get; init; } = true;

    public string AutomationAdvancePolicy { get; init; } = "full-auto-loop";

    public int AutomationPollIntervalMilliseconds { get; init; } = 1500;

    public int AutomationCaptureLines { get; init; } = 220;

    public int AutomationTimeoutSeconds { get; init; } = 600;

    public int AutomationMaxAutoStages { get; init; } = 8;

    public int AutomationMaxRetryPerStage { get; init; } = 2;

    public bool AutomationSubmitOnSend { get; init; } = true;

    public bool IsBuiltIn { get; init; }

    public bool DefaultUseWorktree { get; init; }

    public string WorktreeStrategy { get; init; } = "none";

    public string DefaultWorktreeStrategy { get; init; } = "none";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
}
