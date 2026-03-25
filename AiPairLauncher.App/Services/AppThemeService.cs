using System.Windows;
using MediaColor = System.Windows.Media.Color;
using System.Windows.Media;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.Services;

public sealed class AppThemeService : IAppThemeService
{
    private const string ThemePreferenceKey = "ui_theme_mode";

    private readonly ResourceDictionary _resources;
    private readonly ISessionRepository _sessionRepository;
    private ThemeMode _currentTheme = ThemeMode.Light;

    public AppThemeService(ResourceDictionary resources, ISessionRepository sessionRepository)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
    }

    public ThemeMode GetCurrentTheme()
    {
        return _currentTheme;
    }

    public void ApplyTheme(ThemeMode mode)
    {
        var palette = GetPalette(mode);
        _currentTheme = mode;

        SetBrush("AppBackgroundBrush", palette.AppBackground);
        SetBrush("CardBackgroundBrush", palette.CardBackground);
        SetBrush("CardBackgroundSoftBrush", palette.CardBackgroundSoft);
        SetBrush("TextPrimaryBrush", palette.TextPrimary);
        SetBrush("TextSecondaryBrush", palette.TextSecondary);
        SetBrush("BorderBrushSoft", palette.BorderSoft);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("SuccessBrush", palette.Success);
        SetBrush("DangerBrush", palette.Danger);
        SetBrush("WarningBrush", palette.Warning);
        SetBrush("MutedBrush", palette.Muted);
        SetBrush("ButtonBackgroundBrush", palette.ButtonBackground);
        SetBrush("ButtonBorderBrush", palette.ButtonBorder);
        SetBrush("InputBackgroundBrush", palette.InputBackground);
        SetBrush("InputBorderBrush", palette.InputBorder);
        SetBrush("ListBadgeBackgroundBrush", palette.ListBadgeBackground);
        SetBrush("InsetBackgroundBrush", palette.InsetBackground);
        SetBrush("GridRowBackgroundBrush", palette.GridRowBackground);
        SetBrush("GridAltRowBackgroundBrush", palette.GridAltRowBackground);
        SetBrush("GridHeaderBackgroundBrush", palette.GridHeaderBackground);
    }

    public async Task<ThemeMode> LoadThemePreferenceAsync(CancellationToken cancellationToken = default)
    {
        var value = await _sessionRepository.GetAppStateAsync(ThemePreferenceKey, cancellationToken).ConfigureAwait(false);
        var mode = ParseThemeMode(value);
        ApplyTheme(mode);
        return mode;
    }

    public async Task SaveThemePreferenceAsync(ThemeMode mode, CancellationToken cancellationToken = default)
    {
        await _sessionRepository.SetAppStateAsync(ThemePreferenceKey, mode.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private void SetBrush(string key, MediaColor color)
    {
        _resources[key] = new SolidColorBrush(color);
    }

    private static ThemeMode ParseThemeMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "dark" => ThemeMode.Dark,
            "light" => ThemeMode.Light,
            "eyecare" => ThemeMode.EyeCare,
            _ => ThemeMode.Light,
        };
    }

    private static ThemePalette GetPalette(ThemeMode mode)
    {
        return mode switch
        {
            ThemeMode.Light => new ThemePalette
            {
                AppBackground = ParseColor("#EEF2F7"),
                CardBackground = ParseColor("#FFFFFF"),
                CardBackgroundSoft = ParseColor("#F6F8FB"),
                TextPrimary = ParseColor("#162033"),
                TextSecondary = ParseColor("#60718A"),
                BorderSoft = ParseColor("#CFD9E7"),
                Accent = ParseColor("#1565C0"),
                Success = ParseColor("#2E7D32"),
                Danger = ParseColor("#C62828"),
                Warning = ParseColor("#B26A00"),
                Muted = ParseColor("#8A97A9"),
                ButtonBackground = ParseColor("#E3ECF9"),
                ButtonBorder = ParseColor("#B9CAE4"),
                InputBackground = ParseColor("#FFFFFF"),
                InputBorder = ParseColor("#C1CCDB"),
                ListBadgeBackground = ParseColor("#DDEAFB"),
                InsetBackground = ParseColor("#F7F9FC"),
                GridRowBackground = ParseColor("#FFFFFF"),
                GridAltRowBackground = ParseColor("#F6F8FB"),
                GridHeaderBackground = ParseColor("#E8EEF7"),
            },
            ThemeMode.EyeCare => new ThemePalette
            {
                AppBackground = ParseColor("#E6E0D0"),
                CardBackground = ParseColor("#F4EEDA"),
                CardBackgroundSoft = ParseColor("#ECE3C8"),
                TextPrimary = ParseColor("#433A2F"),
                TextSecondary = ParseColor("#726554"),
                BorderSoft = ParseColor("#C9BA9C"),
                Accent = ParseColor("#6B8A5E"),
                Success = ParseColor("#5A7D4F"),
                Danger = ParseColor("#B85C38"),
                Warning = ParseColor("#B38B2D"),
                Muted = ParseColor("#9C907C"),
                ButtonBackground = ParseColor("#DDE4CC"),
                ButtonBorder = ParseColor("#AFBA92"),
                InputBackground = ParseColor("#FBF6E8"),
                InputBorder = ParseColor("#CABB9D"),
                ListBadgeBackground = ParseColor("#E1E8D1"),
                InsetBackground = ParseColor("#EEE6D2"),
                GridRowBackground = ParseColor("#F6F0DF"),
                GridAltRowBackground = ParseColor("#EFE6D1"),
                GridHeaderBackground = ParseColor("#E3D8BC"),
            },
            _ => new ThemePalette
            {
                AppBackground = ParseColor("#0A1220"),
                CardBackground = ParseColor("#111B2E"),
                CardBackgroundSoft = ParseColor("#16243A"),
                TextPrimary = ParseColor("#E7EEF8"),
                TextSecondary = ParseColor("#90A4C2"),
                BorderSoft = ParseColor("#223654"),
                Accent = ParseColor("#5DC7FF"),
                Success = ParseColor("#45D483"),
                Danger = ParseColor("#FF6B6B"),
                Warning = ParseColor("#FFC857"),
                Muted = ParseColor("#62718B"),
                ButtonBackground = ParseColor("#193252"),
                ButtonBorder = ParseColor("#2A4971"),
                InputBackground = ParseColor("#0E1728"),
                InputBorder = ParseColor("#29425F"),
                ListBadgeBackground = ParseColor("#203654"),
                InsetBackground = ParseColor("#101A2B"),
                GridRowBackground = ParseColor("#102038"),
                GridAltRowBackground = ParseColor("#0D1A2C"),
                GridHeaderBackground = ParseColor("#13233B"),
            },
        };
    }

    private static MediaColor ParseColor(string hex)
    {
        return (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
    }

    private sealed class ThemePalette
    {
        public required MediaColor AppBackground { get; init; }
        public required MediaColor CardBackground { get; init; }
        public required MediaColor CardBackgroundSoft { get; init; }
        public required MediaColor TextPrimary { get; init; }
        public required MediaColor TextSecondary { get; init; }
        public required MediaColor BorderSoft { get; init; }
        public required MediaColor Accent { get; init; }
        public required MediaColor Success { get; init; }
        public required MediaColor Danger { get; init; }
        public required MediaColor Warning { get; init; }
        public required MediaColor Muted { get; init; }
        public required MediaColor ButtonBackground { get; init; }
        public required MediaColor ButtonBorder { get; init; }
        public required MediaColor InputBackground { get; init; }
        public required MediaColor InputBorder { get; init; }
        public required MediaColor ListBadgeBackground { get; init; }
        public required MediaColor InsetBackground { get; init; }
        public required MediaColor GridRowBackground { get; init; }
        public required MediaColor GridAltRowBackground { get; init; }
        public required MediaColor GridHeaderBackground { get; init; }
    }
}
