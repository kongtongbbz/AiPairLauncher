namespace AiPairLauncher.App.Services;

public sealed class SessionRuntimeRegistry : ISessionRuntimeRegistry
{
    private readonly IAutoCollaborationCoordinatorFactory _coordinatorFactory;
    private readonly Dictionary<string, IAutoCollaborationCoordinator> _automationCoordinators = new(StringComparer.Ordinal);

    public SessionRuntimeRegistry(IAutoCollaborationCoordinatorFactory coordinatorFactory)
    {
        _coordinatorFactory = coordinatorFactory ?? throw new ArgumentNullException(nameof(coordinatorFactory));
    }

    public IAutoCollaborationCoordinator GetOrCreateAutomationCoordinator(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (_automationCoordinators.TryGetValue(sessionId, out var coordinator))
        {
            return coordinator;
        }

        coordinator = _coordinatorFactory.Create();
        _automationCoordinators[sessionId] = coordinator;
        return coordinator;
    }

    public bool TryGetAutomationCoordinator(string sessionId, out IAutoCollaborationCoordinator? coordinator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _automationCoordinators.TryGetValue(sessionId, out coordinator);
    }

    public async Task StopAutomationAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!_automationCoordinators.Remove(sessionId, out var coordinator))
        {
            return;
        }

        await coordinator.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        var sessionIds = _automationCoordinators.Keys.ToArray();
        foreach (var sessionId in sessionIds)
        {
            await StopAutomationAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
    }
}
