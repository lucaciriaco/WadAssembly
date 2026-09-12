namespace WadAssembly.App.ViewModels;

using Avalonia.Media;

/// <summary>One slot row of the plan sheet. A row is either occupied (a map WAD is assigned
/// to the slot) or empty (free slot placeholder). Empty rows are skipped when compiling.</summary>
public sealed class SlotRowViewModel : ObservableObject
{
    /// <summary>Sentinel meaning "no music", resolved in the current UI language.</summary>
    public static string NoMusicOption => WadAssembly.App.Services.LanguageService.GetString("NoMusic");

    /// <summary>Status options shown in the per-slot status dropdown.</summary>
    public static readonly string[] StatusOptions = { "TODO", "WIP", "DONE", "FIX" };

    private static readonly IBrush NormalBackground = Brushes.Transparent;
    private static readonly IBrush DropHighlightBackground = new SolidColorBrush(Color.FromArgb(0x66, 0x1E, 0x90, 0xFF));

    private string _slotName = "";
    private string _levelName = "";
    private string _musicName = "";
    private string _musicExternalPath = "";
    private string _skyName = "";
    private string _sky2Name = "";
    private double _skyScroll;
    private double _sky2Scroll;
    private bool _enableSky2;
    private string _notes = "";
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
        set
        {
            if (SetProperty(ref _skyName, value))
                OnPropertyChanged(nameof(SkyDisplay));
        }
    }

    /// <summary>Sky texture name for the optional second sky layer (e.g., SKYFOG); used by the generated MAPINFO.</summary>
    public string Sky2Name
    {
        get => _sky2Name;
        set
        {
            if (SetProperty(ref _sky2Name, value))
                OnPropertyChanged(nameof(SkyDisplay));
        }
    }

    /// <summary>Horizontal rotation speed of the sky (0 = static). Persisted in MAPINFO as the sky1/sky2 offset.</summary>
    public double SkyScroll
    {
        get => _skyScroll;
        set
        {
            if (SetProperty(ref _skyScroll, value))
                OnPropertyChanged(nameof(SkyDisplay));
        }
    }

    /// <summary>Horizontal rotation speed of the sky2 layer (0 = static).</summary>
    public double Sky2Scroll
    {
        get => _sky2Scroll;
        set
        {
            if (SetProperty(ref _sky2Scroll, value))
                OnPropertyChanged(nameof(SkyDisplay));
        }
    }

    /// <summary>When true, a second sky layer (Sky2) is written to the MAPINFO.</summary>
    public bool EnableSky2
    {
        get => _enableSky2;
        set
        {
            if (SetProperty(ref _enableSky2, value))
                OnPropertyChanged(nameof(SkyDisplay));
        }
    }

    /// <summary>Text shown by the sky picker button: "SKY1 0.5" with the rotation speed,
    /// or "SKY1 0.5 / SKY2 0.3" when the second layer is enabled.</summary>
    public string SkyDisplay
    {
        get
        {
            string s1 = string.IsNullOrWhiteSpace(SkyName) ? "sky1" : SkyName.Trim();
            string display = $"{s1}  {SkyScroll:0.##}";
            if (EnableSky2 && !string.IsNullOrWhiteSpace(Sky2Name))
                display += $" / {Sky2Name.Trim()}  {Sky2Scroll:0.##}";
            return display;
        }
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
                : $"{MusicName}  {WadAssembly.App.Services.LanguageService.GetString("Music.ExtMark")}";
        }
    }

    /// <summary>Author of the map, written as a comment above the map block in the MAPINFO.</summary>
    public string Author
    {
        get => _author;
        set => SetProperty(ref _author, value);
    }
    /// <summary>Additional free-text notes for this slot (visible in the grid, stored in
    /// the project JSON; not written to the generated PWAD).</summary>
    public string Notes
    {
        get => _notes;
        set
        {
            if (SetProperty(ref _notes, value))
                OnPropertyChanged(nameof(NotesDisplay));
        }
    }

    /// <summary>Text shown by the notes button: a single-line preview of the notes, or
    /// the localized placeholder while the slot has no notes.</summary>
    public string NotesDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Notes))
                return WadAssembly.App.Services.LanguageService.GetString("Notes.Placeholder");
            return Notes.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    /// <summary>Progress status of the map (TODO/WIP/DONE/FIX), written as a comment in the MAPINFO.</summary>
    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(RowBackground));
        }
    }

    /// <summary>Global toggle of the "color rows by status" plan-sheet preference, so rows
    /// created after the toggle still honor it without per-row syncing. Mirrors the per-project
    /// <c>ColorByStatus</c> property of the main view model.</summary>
    public static bool ColorByStatusEnabled { get; set; }

    private static readonly IBrush TodoBackground = new SolidColorBrush(Color.Parse("#F28B82"));
    private static readonly IBrush WipBackground = new SolidColorBrush(Color.Parse("#FDD663"));
    private static readonly IBrush DoneBackground = new SolidColorBrush(Color.Parse("#81C995"));
    private static readonly IBrush FixBackground = new SolidColorBrush(Color.Parse("#FABE78"));

    /// <summary>Re-evaluates the row background after the global status-color toggle changes.</summary>
    internal void RefreshStatusColors() => OnPropertyChanged(nameof(RowBackground));

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

    /// <summary>Raises property-changed for the localized <see cref="MusicDisplay"/>, and the
    /// sky display (the number formatting is culture-invariant, but refresh keeps it aligned after a language switch).</summary>
    internal void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(MusicDisplay));
        OnPropertyChanged(nameof(SkyDisplay));
        OnPropertyChanged(nameof(NotesDisplay));
    }

    /// <summary>Background brush of the row: transparent normally, blue while this row is
    /// the highlighted drop target of a drag, or the status color of the row when
    /// <see cref="ColorByStatusEnabled"/> and the status is one of TODO/WIP/DONE/FIX.</summary>
    public IBrush RowBackground
    {
        get
        {
            if (IsDropTarget)
                return DropHighlightBackground;
            if (!ColorByStatusEnabled)
                return NormalBackground;
            return Status switch
            {
                "TODO" => TodoBackground,
                "WIP" => WipBackground,
                "DONE" => DoneBackground,
                "FIX" => FixBackground,
                _ => NormalBackground,
            };
        }
    }
}