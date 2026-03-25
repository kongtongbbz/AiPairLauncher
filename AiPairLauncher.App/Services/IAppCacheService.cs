using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface IAppCacheService
{
    Task<AppCacheCleanupResult> ClearAsync(CancellationToken cancellationToken = default);
}
