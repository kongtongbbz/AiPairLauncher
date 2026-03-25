using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public interface IAppThemeService
{
    ThemeMode GetCurrentTheme();

    void ApplyTheme(ThemeMode mode);

    Task<ThemeMode> LoadThemePreferenceAsync(CancellationToken cancellationToken = default);

    Task SaveThemePreferenceAsync(ThemeMode mode, CancellationToken cancellationToken = default);
}
