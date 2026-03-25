using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface ISessionStore : ISessionRepository
{
    string DatabasePath => StateDatabasePath;

    Task SaveAsync(LauncherSession session, CancellationToken cancellationToken = default);

    Task<LauncherSession?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAllAsync(
        IReadOnlyList<ManagedSessionRecord> sessionRecords,
        string? selectedSessionId = null,
        CancellationToken cancellationToken = default);

    Task SelectAsync(string sessionId, CancellationToken cancellationToken = default);
}
