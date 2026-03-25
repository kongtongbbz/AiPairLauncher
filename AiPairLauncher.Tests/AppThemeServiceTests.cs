using System.IO;
using System.Windows;
using System.Windows.Media;
using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class AppThemeServiceTests : IDisposable
{
    private readonly string _rootPath;

    public AppThemeServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "AiPairLauncher.ThemeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
    }

    [Fact(DisplayName = "test_theme_mode_persists_to_app_state")]
    public async Task ThemeModePersistsToAppStateAsync()
    {
        var store = new SessionStore(_rootPath);
        var service = new AppThemeService(CreateResourceDictionary(), store);

        await service.SaveThemePreferenceAsync(ThemeMode.EyeCare);

        var rawValue = await store.GetAppStateAsync("ui_theme_mode");
        Assert.Equal("EyeCare", rawValue);
    }

    [Fact(DisplayName = "test_app_startup_loads_saved_theme")]
    public async Task AppStartupLoadsSavedThemeAsync()
    {
        var store = new SessionStore(_rootPath);
        await store.SetAppStateAsync("ui_theme_mode", "Light");
        var resources = CreateResourceDictionary();
        var service = new AppThemeService(resources, store);

        var theme = await service.LoadThemePreferenceAsync();

        Assert.Equal(ThemeMode.Light, theme);
        Assert.Equal(ThemeMode.Light, service.GetCurrentTheme());
        Assert.Equal(ParseColor("#EEF2F7"), GetBrushColor(resources, "AppBackgroundBrush"));
    }

    [Fact(DisplayName = "test_app_startup_defaults_to_light_theme_when_preference_missing")]
    public async Task AppStartupDefaultsToLightThemeWhenPreferenceMissingAsync()
    {
        var store = new SessionStore(_rootPath);
        var resources = CreateResourceDictionary();
        var service = new AppThemeService(resources, store);

        var theme = await service.LoadThemePreferenceAsync();

        Assert.Equal(ThemeMode.Light, theme);
        Assert.Equal(ParseColor("#EEF2F7"), GetBrushColor(resources, "AppBackgroundBrush"));
    }

    [Fact(DisplayName = "test_switch_theme_updates_brush_keys_without_restart")]
    public void SwitchThemeUpdatesBrushKeysWithoutRestartAsync()
    {
        var resources = CreateResourceDictionary();
        var service = new AppThemeService(resources, new SessionStore(_rootPath));

        service.ApplyTheme(ThemeMode.Dark);
        var darkBackground = GetBrushColor(resources, "AppBackgroundBrush");
        service.ApplyTheme(ThemeMode.Light);
        var lightBackground = GetBrushColor(resources, "AppBackgroundBrush");

        Assert.NotEqual(darkBackground, lightBackground);
        Assert.Equal(ParseColor("#EEF2F7"), lightBackground);
    }

    [Fact(DisplayName = "test_eye_care_theme_uses_distinct_palette_not_alias_of_light")]
    public void EyeCareThemeUsesDistinctPaletteNotAliasOfLightAsync()
    {
        var resources = CreateResourceDictionary();
        var service = new AppThemeService(resources, new SessionStore(_rootPath));

        service.ApplyTheme(ThemeMode.Light);
        var lightCard = GetBrushColor(resources, "CardBackgroundBrush");
        var lightAccent = GetBrushColor(resources, "AccentBrush");

        service.ApplyTheme(ThemeMode.EyeCare);
        var eyeCard = GetBrushColor(resources, "CardBackgroundBrush");
        var eyeAccent = GetBrushColor(resources, "AccentBrush");

        Assert.NotEqual(lightCard, eyeCard);
        Assert.NotEqual(lightAccent, eyeAccent);
        Assert.Equal(ParseColor("#F4EEDA"), eyeCard);
    }

    [Fact(DisplayName = "test_switch_theme_replaces_frozen_brush_resources")]
    public void SwitchThemeReplacesFrozenBrushResourcesAsync()
    {
        var resources = CreateResourceDictionary();
        foreach (var key in resources.Keys.Cast<object>().ToArray())
        {
            if (resources[key] is SolidColorBrush brush && brush.CanFreeze)
            {
                brush.Freeze();
            }
        }

        var service = new AppThemeService(resources, new SessionStore(_rootPath));

        service.ApplyTheme(ThemeMode.Light);

        Assert.Equal(ParseColor("#EEF2F7"), GetBrushColor(resources, "AppBackgroundBrush"));
        Assert.False(((SolidColorBrush)resources["AppBackgroundBrush"]).IsFrozen);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private static ResourceDictionary CreateResourceDictionary()
    {
        static SolidColorBrush Brush(string hex) => new(ParseColor(hex));

        return new ResourceDictionary
        {
            ["AppBackgroundBrush"] = Brush("#0A1220"),
            ["CardBackgroundBrush"] = Brush("#111B2E"),
            ["CardBackgroundSoftBrush"] = Brush("#16243A"),
            ["TextPrimaryBrush"] = Brush("#E7EEF8"),
            ["TextSecondaryBrush"] = Brush("#90A4C2"),
            ["BorderBrushSoft"] = Brush("#223654"),
            ["AccentBrush"] = Brush("#5DC7FF"),
            ["SuccessBrush"] = Brush("#45D483"),
            ["DangerBrush"] = Brush("#FF6B6B"),
            ["WarningBrush"] = Brush("#FFC857"),
            ["MutedBrush"] = Brush("#62718B"),
            ["ButtonBackgroundBrush"] = Brush("#193252"),
            ["ButtonBorderBrush"] = Brush("#2A4971"),
            ["InputBackgroundBrush"] = Brush("#0E1728"),
            ["InputBorderBrush"] = Brush("#29425F"),
            ["ListBadgeBackgroundBrush"] = Brush("#203654"),
            ["InsetBackgroundBrush"] = Brush("#101A2B"),
            ["GridRowBackgroundBrush"] = Brush("#102038"),
            ["GridAltRowBackgroundBrush"] = Brush("#0D1A2C"),
            ["GridHeaderBackgroundBrush"] = Brush("#13233B"),
        };
    }

    private static Color GetBrushColor(ResourceDictionary resources, string key)
    {
        return ((SolidColorBrush)resources[key]).Color;
    }

    private static Color ParseColor(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex)!;
    }
}
