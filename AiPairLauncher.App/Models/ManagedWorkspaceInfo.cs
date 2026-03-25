namespace AiPairLauncher.App.Models;

public sealed class ManagedWorkspaceInfo
{
    public string Workspace { get; init; } = string.Empty;

    public string SocketPath { get; init; } = string.Empty;

    public int GuiPid { get; init; }

    public IReadOnlyList<PaneInfo> Panes { get; init; } = [];
}
