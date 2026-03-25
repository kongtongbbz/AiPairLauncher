using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public static class SessionPaneRouter
{
    public static (int SourcePaneId, int TargetPaneId) ResolveTransferPaneIds(LauncherSession session, SendContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourcePaneId is int sourcePaneId && request.TargetPaneId is int targetPaneId)
        {
            return (sourcePaneId, targetPaneId);
        }

        return request.FromLeftToRight
            ? (session.LeftPaneId, session.RightPaneId)
            : (session.RightPaneId, session.LeftPaneId);
    }
}
