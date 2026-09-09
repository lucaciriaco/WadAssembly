using Avalonia;
using Avalonia.Controls;
using CommunityWadCompiler.App.Resources;
using CommunityWadCompiler.Core.Localization;

namespace CommunityWadCompiler.App.Services;

/// <summary>
/// Manages the UI language at runtime by swapping the merged application resource
/// dictionary (es / en). The default language is English; the choice is persisted in
/// the application settings JSON (migrated from the old registry value on first run).
/// XAML binds to strings with {DynamicResource ...}, which re-resolve automatically
/// when the dictionary is swapped.
/// </summary>
public static class LanguageService
{
    public const string Spanish = "es";
    public const string English = "en";

    private const string LegacyRegKey = @"HKEY_CURRENT_USER\Software\CommunityWadCompiler";
    private const string LegacyRegValue = "Language";

    private static string _current = English;
    private static bool _loaded;

    /// <summary>Snapshot of the Core console messages translated for the current language.
    /// Composed on the UI thread when the dictionaries reload; served from worker threads
    /// (the merge pipeline) so Avalonia resources are never touched off the UI thread.</summary>
    private static Dictionary<string, string> _activeCore = new(StringComparer.Ordinal);

    /// <summary>Current two-letter language code ("en" or "es").</summary>
    public static string CurrentLanguage => _current;

    /// <summary>Raised after the UI language changes; views can refresh computed strings.</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>Switches the UI language and persists the choice in the settings JSON.</summary>
    public static void SetLanguage(string language)
    {
        string lang = Normalize(language);
        if (!_loaded || lang != _current)
        {
            _current = lang;
            ReloadDictionaries(lang);
            SaveLanguage(lang);
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Restores the persisted language (if any). Call once before creating the main window.</summary>
    public static void LoadPersistedLanguage()
    {
        string? lang = NormalizeOrNull(AppSettingsService.Load().Language);
        if (lang is null)
            lang = ReadLegacyRegistryLanguage(); // migrate the pre-JSON choice into the config on first run
        if (lang is not null)
            _current = lang;

        // Keep the backing dictionaries in sync with the default (English).
        ReloadDictionaries(_current);
        _loaded = true;
    }

    /// <summary>Looks up a localized string by key; returns the key itself if not found.</summary>
    public static string GetString(string key)
    {
        if (Application.Current is not null &&
            Application.Current.TryFindResource(key, out var value) &&
            value is string s)
            return s;
        return key;
    }

    /// <summary>Looks up a localized string by key, using <paramref name="fallback"/> when
    /// the key is not present (used by Core's message resolver). Core messages are resolved
    /// through the <see cref="_activeCore"/> snapshot built on the UI thread; everything else
    /// falls back to the live Avalonia resources.</summary>
    public static string GetString(string key, string fallback)
    {
        if (_activeCore.TryGetValue(key, out string? cached))
            return cached;
        string value = GetString(key);
        return value == key ? fallback : value;
    }

    private static void SaveLanguage(string language)
    {
        var settings = AppSettingsService.Load();
        settings.Language = language;
        AppSettingsService.Save(settings);
    }

    private static string? ReadLegacyRegistryLanguage()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return Microsoft.Win32.Registry.GetValue(LegacyRegKey, LegacyRegValue, null) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string Normalize(string language)
        => language == English ? English : Spanish;

    private static string? NormalizeOrNull(string? language)
        => language is English or Spanish ? language : null;

    private static void ReloadDictionaries(string language)
    {
        if (Application.Current is null)
            return;
        IResourceProvider dictionary = language == English
            ? new Strings_en()
            : new Strings_es();
        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(dictionary);
        RebuildCoreSnapshot();
    }

    /// <summary>Translates every Core console message for the current language. Runs on the
    /// UI thread (Avalonia resource lookups) and swaps the snapshot atomically so worker
    /// threads always read a complete, consistent picture.</summary>
    private static void RebuildCoreSnapshot()
    {
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in CoreMessages.Keys)
        {
            string value = GetString(key);
            if (value != key)
                next[key] = value;
        }
        _activeCore = next;
    }
}