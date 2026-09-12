using System.Text.Json;
using WadAssembly.App.Models;

namespace WadAssembly.App.Services;

/// <summary>Loads and saves the application settings JSON file
/// (<c>%APPDATA%\WadAssembly\config.json</c>). IO failures degrade to defaults.</summary>
public static class AppSettingsService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WadAssembly");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    public static string ConfigPath => ConfigFile;

    /// <summary>Reads the settings; returns defaults when the file is missing or corrupt.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigFile));
                if (settings is not null)
                {
                    // Config files saved before RecentProjects existed have a null list.
                    settings.RecentProjects ??= new();
                    return settings;
                }
            }
        }
        catch
        {
            // A corrupt settings file must not prevent the app from starting.
        }
        return new AppSettings();
    }

    /// <summary>Writes the settings; failures are ignored so the UI keeps working.</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }
        catch
        {
            // Restricted environments (e.g. read-only profile) should not crash the app.
        }
    }
}