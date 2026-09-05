namespace CommunityWadCompiler.App.ViewModels;

/// <summary>One slot row of the plan sheet. A row is either occupied (a map WAD is assigned
/// to the slot) or empty (free slot placeholder). Empty rows are skipped when compiling.</summary>
public sealed class SlotRowViewModel : ObservableObject
{
    /// <summary>Sentinel shown as the last entry of the music dropdown meaning "no music".</summary>
    public const string NoMusicOption = "Ninguna";

    /// <summary>Status options shown in the per-slot status dropdown.</summary>
    public static readonly string[] StatusOptions = { "TODO", "WIP", "DONE", "FIX" };

    private string _slotName = "";
    private string _levelName = "";
    private string _musicName = "";
    private string _author = "";
    private string _status = "";

    /// <summary>Creates a slot row. Pass <paramref name="wadPath"/> = null for an empty slot.</summary>
    public SlotRowViewModel(string? wadPath, string? originalName, bool isUdmf)
    {
        WadPath = wadPath;
        OriginalName = originalName;
        IsUdmf = isUdmf;
        LastModified = wadPath is not null && File.Exists(wadPath)
            ? File.GetLastWriteTime(wadPath).ToString("dd/MM/yyyy HH:mm")
            : "";
    }

    /// <summary>True when no map is assigned to this slot yet.</summary>
    public bool IsEmpty => WadPath is null;

    /// <summary>Path of the WAD providing the map; null for an empty slot.</summary>
    public string? WadPath { get; }

    /// <summary>Original lump name of the map in its WAD; null for an empty slot.</summary>
    public string? OriginalName { get; }

    public bool IsUdmf { get; }

    public string WadFileName => Path.GetFileNameWithoutExtension(WadPath ?? "");

    /// <summary>Last write time of the source WAD file (when the map was last updated).</summary>
    public string LastModified { get; }

    /// <summary>Music lumps available to pick from (shared list maintained by the view model).</summary>
    public IReadOnlyList<string>? MusicOptions { get; set; }

    /// <summary>Final slot name (MAP01, MAP02, ...); usually auto-assigned by row position.</summary>
    public string SlotName
    {
        get => _slotName;
        set => SetProperty(ref _slotName, value);
    }

    /// <summary>Readable level name shown on the automap; used by the generated MAPINFO.</summary>
    public string LevelName
    {
        get => _levelName;
        set => SetProperty(ref _levelName, value);
    }

    /// <summary>Music lump assigned to this slot; used by the generated MAPINFO.</summary>
    public string MusicName
    {
        get => _musicName;
        set => SetProperty(ref _musicName, value);
    }

    /// <summary>Author of the map, written as a comment above the map block in the MAPINFO.</summary>
    public string Author
    {
        get => _author;
        set => SetProperty(ref _author, value);
    }

    /// <summary>Progress status of the map (TODO/WIP/DONE/FIX), written as a comment in the MAPINFO.</summary>
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}