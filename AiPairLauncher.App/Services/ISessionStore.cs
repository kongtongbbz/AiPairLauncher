using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface ISessionStore
{
    string SessionFilePath { get; }

    Task SaveAsync(LauncherSession session, CancellationToken cancellationToken = default);

    Task<LauncherSession?> LoadAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

