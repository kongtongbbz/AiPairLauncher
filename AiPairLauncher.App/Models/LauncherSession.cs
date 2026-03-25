namespace AiPairLauncher.App.Models;

public sealed class LauncherSession
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

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

    public LauncherSession Clone()
    {
        return new LauncherSession
        {
            SessionId = SessionId,
            Workspace = Workspace,
            WorkingDirectory = WorkingDirectory,
            WezTermPath = WezTermPath,
            SocketPath = SocketPath,
            GuiPid = GuiPid,
            LeftPaneId = LeftPaneId,
            RightPaneId = RightPaneId,
            RightPanePercent = RightPanePercent,
            ClaudeObserverPaneId = ClaudeObserverPaneId,
            CodexObserverPaneId = CodexObserverPaneId,
            AutomationObserverEnabled = AutomationObserverEnabled,
            ClaudePermissionMode = ClaudePermissionMode,
            CodexMode = CodexMode,
            AutomationEnabledAtLaunch = AutomationEnabledAtLaunch,
            CreatedAt = CreatedAt,
        };
    }
}
