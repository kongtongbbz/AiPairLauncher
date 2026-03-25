namespace AiPairLauncher.App.Models;

public sealed class LaunchRequest
{
    public string? Workspace { get; init; }

    public required string WorkingDirectory { get; init; }

    public string ClaudePermissionMode { get; init; } = "default";

    public string CodexMode { get; init; } = "standard";

    public bool AutomationEnabled { get; init; }

    public bool AutomationObserverEnabled { get; init; } = true;

    public int RightPanePercent { get; init; } = 60;

    public int StartupTimeoutSeconds { get; init; } = 20;
}
