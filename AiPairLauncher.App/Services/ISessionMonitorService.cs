using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface ISessionMonitorService
{
    Task<IReadOnlyList<ManagedSessionRecord>> RefreshAsync(
        IReadOnlyList<ManagedSessionRecord> sessionRecords,
        Func<string, AutomationRunState?> automationStateResolver,
        CancellationToken cancellationToken = default);
}
