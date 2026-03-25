using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public static class LauncherSessionValidator
{
    public static void EnsurePaneTopology(LauncherSession session, IReadOnlyList<PaneInfo> panes)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(panes);

        if (panes.Count == 0)
        {
            throw new InvalidOperationException($"工作区 {session.Workspace} 当前没有任何可用 pane。");
        }

        var paneIds = panes
            .Select(static pane => pane.PaneId)
            .ToHashSet();

        if (!paneIds.Contains(session.LeftPaneId) || !paneIds.Contains(session.RightPaneId))
        {
            throw new InvalidOperationException(
                $"最近会话记录的 pane 已失效。当前工作区 pane: {string.Join(", ", paneIds.OrderBy(static id => id))}，" +
                $"记录的 Left={session.LeftPaneId}, Right={session.RightPaneId}。");
        }
    }
}
