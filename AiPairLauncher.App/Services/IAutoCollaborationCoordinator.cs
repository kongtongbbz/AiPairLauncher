using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface IAutoCollaborationCoordinator
{
    event EventHandler<AutomationRunState>? StateChanged;

    AutomationRunState GetCurrentState();

    Task StartAsync(LauncherSession session, AutomationSettings settings, CancellationToken cancellationToken = default);

    Task RestoreAsync(
        LauncherSession session,
        AutomationSettings settings,
        AutomationRunState state,
        CancellationToken cancellationToken = default);

    Task ApproveAsync(string? userNote, CancellationToken cancellationToken = default);

    Task RejectAsync(string? userNote, CancellationToken cancellationToken = default);

    Task ContinueWaitingAsync(CancellationToken cancellationToken = default);

    Task RetryCurrentStageAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
