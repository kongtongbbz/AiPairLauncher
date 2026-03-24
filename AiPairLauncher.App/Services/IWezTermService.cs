using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface IWezTermService
{
    Task<LauncherSession> StartAiPairAsync(LaunchRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaneInfo>> GetWorkspacePanesAsync(LauncherSession session, string workspace, CancellationToken cancellationToken = default);

    Task<ContextTransferResult> SendContextAsync(LauncherSession session, SendContextRequest request, CancellationToken cancellationToken = default);
}

