namespace CommunityWadCompiler.App.ViewModels;

using Avalonia.Media;

/// <summary>One slot row of the plan sheet. A row is either occupied (a map WAD is assigned
/// to the slot) or empty (free slot placeholder). Empty rows are skipped when compiling.</summary>
public sealed class SlotRowViewModel : ObservableObject
{
    /// <summary>Sentinel shown as the last entry of the music dropdown meaning "no music".</summary>
    public const string NoMusicOption = "Ninguna";

    /// <summary>Status options shown in the per-slot status dropdown.</summary>
    public static readonly string[] StatusOptions = { "TODO", "WIP", "DONE", "FIX" };

    private static readonly IBrush NormalBackground = Brushes.Transparent;
    private static readonly IBrush DropHighlightBackground = new SolidColorBrush(Color.FromArgb(0x66, 0x1E, 0x90, 0xFF));

    private string _slotName = "";
    private string _levelName = "";
    private string _musicName = "";
    private string _musicExternalPath = "";
    private string _skyName = "";
    private string _author = "";
    private string _status = "";
    private bool _isDropTarget;

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

    /// <summary>Music options available to pick from (shared list maintained by the view model).</summary>
    public IReadOnlyList<MusicLumpInfo>? MusicOptions { get; set; }

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
        set
        {
            if (SetProperty(ref _musicName, value))
                OnPropertyChanged(nameof(MusicDisplay));
        }
    }

    /// <summary>
    /// Absolute path of an external music file (.mid/.mod/.it) chosen by the user.
    /// Empty when music comes from a WAD lump or is unset.
    /// </summary>
    public string MusicExternalPath
    {
        get => _musicExternalPath;
        set
        {
            if (SetProperty(ref _musicExternalPath, value))
                OnPropertyChanged(nameof(MusicDisplay));
        }
    }

    /// <summary>Sky texture name for this map (e.g., SKY1); used by the generated MAPINFO.</summary>
    public string SkyName
    {
        get => _skyName;
        set => SetProperty(ref _skyName, value);
    }

    /// <summary>Text shown by the music picker button: the assigned lump, or "Ninguna".</summary>
    public string MusicDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MusicName))
                return NoMusicOption;
            return string.IsNullOrWhiteSpace(MusicExternalPath)
                ? MusicName
                : $"{MusicName}  [ext]";
        }
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

    /// <summary>True while this row is the highlighted drop target of a drag operation
    /// (managed by the view model's <c>DropTargetIndex</c>).</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (SetProperty(ref _isDropTarget, value))
                OnPropertyChanged(nameof(RowBackground));
        }
    }

    /// <summary>Background brush of the row: transparent normally, blue while this row is
    /// the highlighted drop target of a drag.</summary>
    public IBrush RowBackground => IsDropTarget ? DropHighlightBackground : NormalBackground;
}