using Avalonia;
using Avalonia.Styling;

namespace WadAssembly.App.Services;

/// <summary>
/// Manages the UI theme (system / light / dark) by setting the application's
/// requested theme variant. The choice is persisted in the application settings
/// JSON; the default is to follow the operating-system setting.
/// </summary>
public static class ThemeService
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    private static string _current = System;

    /// <summary>Current theme ("system", "light" or "dark").</summary>
    public static string CurrentTheme => _current;

    /// <summary>Restores the persisted theme (if any). Call once during startup,
    /// before showing the main window.</summary>
    public static void LoadPersistedTheme()
    {
        string? theme = NormalizeOrNull(AppSettingsService.Load().Theme);
        if (theme is not null)
            _current = theme;
        Apply();
    }

    /// <summary>Switches the UI theme and persists the choice in the settings JSON.</summary>
    public static void SetTheme(string theme)
    {
        string normalized = Normalize(theme);
        if (_current == normalized)
            return;
        _current = normalized;
        Apply();
        SaveTheme(normalized);
    }

    private static void Apply()
    {
        if (Application.Current is not { } app)
            return;
        app.RequestedThemeVariant = _current switch
        {
            Dark => ThemeVariant.Dark,
            Light => ThemeVariant.Light,
            _ => ThemeVariant.Default, // follow the operating system
        };
    }

    private static string Normalize(string theme)
        => theme == Light ? Light : theme == Dark ? Dark : System;

    private static string? NormalizeOrNull(string? theme)
        => theme is System or Light or Dark ? theme : null;

    private static void SaveTheme(string theme)
    {
        var settings = AppSettingsService.Load();
        settings.Theme = theme;
        AppSettingsService.Save(settings);
    }
}