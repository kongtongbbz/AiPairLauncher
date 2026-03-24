using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface IDependencyService
{
    Task<IReadOnlyList<DependencyStatus>> CheckAllAsync(CancellationToken cancellationToken = default);
}

