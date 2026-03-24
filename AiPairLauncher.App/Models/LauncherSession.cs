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

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}

