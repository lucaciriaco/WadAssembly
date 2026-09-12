namespace WadAssembly.App.Models;

/// <summary>Serializable application settings (UI language and the slot plan grid
/// layout), saved as a JSON file under the user's profile.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>Two-letter UI language code ("en" or "es"). Empty when never set,
    /// so the legacy registry value can be migrated in.</summary>
    public string Language { get; set; } = "";

    /// <summary>UI theme ("system", "light" or "dark"). Empty means "system".</summary>
    public string Theme { get; set; } = "";

    /// <summary>Width of each physical column (10 values, pixels). Follows the columns
    /// when they are reordered.</summary>
    public double[] ColumnWidths { get; set; } = Array.Empty<double>();

    /// <summary>Column ids (0..9) shown at each physical position.</summary>
    public int[] ColumnOrder { get; set; } = Array.Empty<int>();

    /// <summary>Visibility of each physical column (10 values, true = shown). Follows
    /// the columns when they are reordered.</summary>
    public bool[] ColumnVisibility { get; set; } = Array.Empty<bool>();

    /// <summary>Whether the bottom console (log) panel is visible. Nullable so that
    /// config files saved before this setting existed default to visible.</summary>
    public bool? LogVisible { get; set; }

    /// <summary>Whether the "pick a map..." hint under the Contributed WADs header is
    /// shown. Nullable so config files saved before this setting default to visible.</summary>
    public bool? ShowWadHint { get; set; }

    /// <summary>Source ports managed from Configuration → Source ports, offered as
    /// launch targets by "Compile and run".</summary>
    public List<SourcePortConfig> SourcePorts { get; set; } = new();

    /// <summary>Behavior of the compile button: "build" or "buildandrun".
    /// Empty defaults to "build", keeping legacy config files working.</summary>
    public string CompileMode { get; set; } = "build";

    /// <summary>Project files opened/saved recently, most recent first. Only the
    /// file path is stored; the entries are offered in File → Open recent.</summary>
    public List<string> RecentProjects { get; set; } = new();
}