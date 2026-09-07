using Avalonia;
using Avalonia.Controls;
using CommunityWadCompiler.App.Resources;

namespace CommunityWadCompiler.App.Services;

/// <summary>
/// Manages the UI language at runtime by swapping the merged application resource
/// dictionary (es / en). The choice is persisted in the Windows registry and restored
/// on startup. XAML binds to strings with {DynamicResource ...}, which re-resolve
/// automatically when the dictionary is swapped.
/// </summary>
public static class LanguageService
{
    public const string Spanish = "es";
    public const string English = "en";

    private const string RegKey = @"HKEY_CURRENT_USER\Software\CommunityWadCompiler";
    private const string RegValue = "Language";

    private static string _current = Spanish;

    /// <summary>Current two-letter language code ("es" or "en").</summary>
    public static string CurrentLanguage => _current;

    /// <summary>Raised after the UI language changes; views can refresh computed strings.</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>Switches the UI language and persists the choice in the registry.</summary>
    public static void SetLanguage(string language)
    {
        string lang = Normalize(language);
        if (lang == _current)
            return;
        _current = lang;
        ReloadDictionaries(lang);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Microsoft.Win32.Registry.SetValue(RegKey, RegValue, lang);
            }
            catch
            {
                // Registry writes may fail in restricted environments; the UI still works.
            }
        }
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Restores the persisted language (if any). Call once before creating the main window.</summary>
    public static void LoadPersistedLanguage()
    {
        string? saved = null;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                saved = Microsoft.Win32.Registry.GetValue(RegKey, RegValue, null) as string;
            }
            catch
            {
                // Fall back to the default language (es).
            }
        }
        if (saved is Spanish or English)
        {
            _current = saved;
            ReloadDictionaries(saved);
        }
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

    private static string Normalize(string language)
        => language == English ? English : Spanish;

    private static void ReloadDictionaries(string language)
    {
        if (Application.Current is null)
            return;
        IResourceProvider dictionary = language == English
            ? new Strings_en()
            : new Strings_es();
        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(dictionary);
    }
}