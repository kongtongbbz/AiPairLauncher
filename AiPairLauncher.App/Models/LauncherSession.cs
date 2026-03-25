namespace AiPairLauncher.App.Models;

public sealed class LauncherSession
{
    public required string Workspace { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string WezTermPath { get; init; }

    public required string SocketPath { get; init; }

    public required int GuiPid { get; init; }

    public required int LeftPaneId { get; init; }

    public required int RightPaneId { get; init; }

    public required int RightPanePercent { get; init; }

    public int? ClaudeObserverPaneId { get; init; }

    public int? CodexObserverPaneId { get; init; }

    public bool AutomationObserverEnabled { get; init; }

    public string ClaudePermissionMode { get; init; } = "default";

    public string CodexMode { get; init; } = "standard";

    public bool AutomationEnabledAtLaunch { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}
