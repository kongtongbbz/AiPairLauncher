namespace AiPairLauncher.App.Services;

public interface ISessionRuntimeRegistry
{
    IAutoCollaborationCoordinator GetOrCreateAutomationCoordinator(string sessionId);

    bool TryGetAutomationCoordinator(string sessionId, out IAutoCollaborationCoordinator? coordinator);

    Task StopAutomationAsync(string sessionId, CancellationToken cancellationToken = default);

    Task StopAllAsync(CancellationToken cancellationToken = default);
}
