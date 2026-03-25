namespace AiPairLauncher.App.Models;

public sealed class LaunchRequest
{
    public string? Workspace { get; init; }

    public required string WorkingDirectory { get; init; }

    public string? ResolvedWorkingDirectory { get; init; }

    public string ClaudePermissionMode { get; init; } = "default";

    public string CodexMode { get; init; } = "standard";

    public bool AutomationEnabled { get; init; }

    public bool AutomationObserverEnabled { get; init; } = true;

    public int RightPanePercent { get; init; } = 60;

    public bool UseWorktree { get; init; }

    public string WorktreeStrategy { get; init; } = "none";

    public string? WorktreeBranchName { get; init; }

    public int StartupTimeoutSeconds { get; init; } = 20;
}
