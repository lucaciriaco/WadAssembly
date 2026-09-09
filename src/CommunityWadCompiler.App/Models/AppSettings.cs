namespace CommunityWadCompiler.App.Models;

/// <summary>Serializable application settings (UI language and the slot plan grid
/// layout), saved as a JSON file under the user's profile.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>Two-letter UI language code ("en" or "es"). Empty when never set,
    /// so the legacy registry value can be migrated in.</summary>
    public string Language { get; set; } = "";

    /// <summary>Width of each physical column (10 values, pixels). Follows the columns
    /// when they are reordered.</summary>
    public double[] ColumnWidths { get; set; } = Array.Empty<double>();

    /// <summary>Column ids (0..9) shown at each physical position.</summary>
    public int[] ColumnOrder { get; set; } = Array.Empty<int>();

    /// <summary>Visibility of each physical column (10 values, true = shown). Follows
    /// the columns when they are reordered.</summary>
    public bool[] ColumnVisibility { get; set; } = Array.Empty<bool>();
}