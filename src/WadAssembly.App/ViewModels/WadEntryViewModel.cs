namespace WadAssembly.App.ViewModels;

using WadAssembly.App.Services;

/// <summary>One map lump inside an input WAD, with its format flag, level name, music and sky from MAPINFO.</summary>
public sealed record MapOption(string OriginalName, bool IsUdmf, string? LevelName = null, string? MusicName = null, string? SkyName = null);

/// <summary>One loaded WAD file shown in the inputs or resources list.</summary>
public sealed class WadEntryViewModel : ObservableObject
{
    private string? _selectedMapName;

    public required string Path { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string WadTypeLabel { get; init; } = "";

    /// <summary>True when this WAD is a resource pack (textures/flats); false for input maps.</summary>
    public bool IsResource { get; init; }

    /// <summary>Maps detected in this WAD (empty for resource packs).</summary>
    public List<MapOption> Maps { get; init; } = new();

    public int MapCount => Maps.Count;

    /// <summary>Shows the map picker dropdown only for WADs with several maps.</summary>
    public bool HasMultipleMaps => Maps.Count > 1;

    /// <summary>Map lump currently selected in the picker; the one dragged into a slot.</summary>
    public string? SelectedMapName
    {
        get => _selectedMapName ?? Maps.FirstOrDefault()?.OriginalName;
        set => SetProperty(ref _selectedMapName, value);
    }

    /// <summary>Lump names exposed to the picker dropdown.</summary>
    public IReadOnlyList<string> MapNames => Maps.Select(m => m.OriginalName).ToList();

    /// <summary>Localized display string for the WAD entry.</summary>
    public string Display
    {
        get
        {
            string detail = Maps.Count > 0 ? string.Format(LanguageService.GetString("Wad.MapCountFormat"), Maps.Count) : "";
            string kind = IsResource
                ? LanguageService.GetString("Wad.Kind.Resources")
                : LanguageService.GetString("Wad.Kind.Input");
            return string.Format(LanguageService.GetString("Wad.DisplayFormat"), FileName, WadTypeLabel, kind, detail);
        }
    }

    /// <summary>Raises property-changed for the localized <see cref="Display"/> after a language switch.</summary>
    internal void RefreshLocalizedText() => OnPropertyChanged(nameof(Display));
}