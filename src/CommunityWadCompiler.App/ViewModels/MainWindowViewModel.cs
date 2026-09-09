using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CommunityWadCompiler.App.Models;
using CommunityWadCompiler.App.Services;
using CommunityWadCompiler.Core;
using CommunityWadCompiler.Core.Maps;
using CommunityWadCompiler.Core.Merge;
using CommunityWadCompiler.Core.Music;
using CommunityWadCompiler.Core.WadFormat;

namespace CommunityWadCompiler.App.ViewModels;

/// <summary>
    /// View model for the main window: holds the list of input WADs, the detected maps
    /// with final slots, output settings and the compile pipeline.
    /// </summary>
    public sealed class MainWindowViewModel : ObservableObject
    {
        private string? _baseWadPath;
        private string? _outputPath;
        private bool _isBusy;
        private bool _autoAssignMaps = true;
        private bool _filterResourcesToUsed = true;
        private WadEntryViewModel? _selectedWad;
        private WadEntryViewModel? _selectedResourceWad;
        private string _projectName = "";
        private string _versionPrefix = "";
private int _slotCount = 32;
    private string? _currentProjectPath;
    private int? _dropTargetIndex;
    private string _intermissionMusic = "";
    private string _intermissionMusicExternalPath = "";
    private SourcePortConfig? _selectedSourcePort;

        public MainWindowViewModel()
        {
            LanguageService.LanguageChanged += OnLanguageChanged;
            // Start with placeholder rows so the sheet is visible before any WAD is loaded.
            for (int i = 0; i < _slotCount; i++)
                SlotRows.Add(new(null, null, false));
            RenumberSlotsByPosition();
        }

        /// <summary>Refreshes every computed, language-dependent string when the UI language
        /// changes. {DynamicResource} bindings refresh by themselves; these are strings
        /// built in code, so their property-changed events must be raised manually.</summary>
        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            foreach (var wad in InputWads)
                wad.RefreshLocalizedText();
            foreach (var wad in ResourceWads)
                wad.RefreshLocalizedText();
            foreach (var row in SlotRows)
                row.RefreshLocalizedText();
            RefreshMusicSentinel();
            OnPropertyChanged(nameof(MapsHeader));
            OnPropertyChanged(nameof(IntermissionMusicDisplay));
        }

        /// <summary>Re-creates the shared "no music" sentinel so its name follows the new language.</summary>
        private void RefreshMusicSentinel()
        {
            for (int i = 0; i < AvailableMusicLumps.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(AvailableMusicLumps[i].WadPath))
                {
                    AvailableMusicLumps[i] = new MusicLumpInfo(SlotRowViewModel.NoMusicOption, "");
                    return;
                }
            }
        }

        public ObservableCollection<WadEntryViewModel> InputWads { get; } = new();

        public ObservableCollection<WadEntryViewModel> ResourceWads { get; } = new();

        /// <summary>Slot rows of the plan sheet: one row per slot (empty slots included).</summary>
        public ObservableCollection<SlotRowViewModel> SlotRows { get; } = new();

        public ObservableCollection<MusicLumpInfo> AvailableMusicLumps { get; } = new();

        /// <summary>Project collaborators (map authors), kept as project metadata only.</summary>
        public ObservableCollection<CollaboratorEntryViewModel> Collaborators { get; } = new();

    public string? BaseWadPath
    {
        get => _baseWadPath;
        set => SetProperty(ref _baseWadPath, value);
    }

    public string ProjectName
    {
        get => _projectName;
        set { if (SetProperty(ref _projectName, value)) OnPropertyChanged(nameof(MapsHeader)); }
    }

    /// <summary>Editable version prefix (e.g. "1.2.3"), shown in the table header and
    /// stamped (prefixed to the compile timestamp) into the MAPINFO.</summary>
    public string VersionPrefix
    {
        get => _versionPrefix;
        set { if (SetProperty(ref _versionPrefix, value)) OnPropertyChanged(nameof(MapsHeader)); }
    }

    /// <summary>Path of the currently loaded/saved project file; null when untitled.
    /// Enables "Guardar proyecto" (overwrite) without opening a dialog.</summary>
    public string? CurrentProjectPath
    {
        get => _currentProjectPath;
        set => SetProperty(ref _currentProjectPath, value);
    }

    public string? OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public bool AutoAssignMaps
    {
        get => _autoAssignMaps;
        set => SetProperty(ref _autoAssignMaps, value);
    }

    public bool FilterResourcesToUsed
    {
        get => _filterResourcesToUsed;
        set => SetProperty(ref _filterResourcesToUsed, value);
    }

    private bool _includePaletteLumps = true;
    public bool IncludePaletteLumps
    {
        get => _includePaletteLumps;
        set => SetProperty(ref _includePaletteLumps, value);
    }

    private bool _includeSpriteLumps;
    public bool IncludeSpriteLumps
    {
        get => _includeSpriteLumps;
        set => SetProperty(ref _includeSpriteLumps, value);
    }

    /// <summary>Lump name of the intermission music chosen in Project Settings
    /// (may hold the "no music" sentinel; normalized when compiling).</summary>
    public string IntermissionMusic
    {
        get => _intermissionMusic;
        set
        {
            if (SetProperty(ref _intermissionMusic, value))
                OnPropertyChanged(nameof(IntermissionMusicDisplay));
        }
    }

    /// <summary>Absolute path of an external music file for the intermission.
    /// Empty when the intermission music comes from a WAD lump or is unset.</summary>
    public string IntermissionMusicExternalPath
    {
        get => _intermissionMusicExternalPath;
        set
        {
            if (SetProperty(ref _intermissionMusicExternalPath, value))
                OnPropertyChanged(nameof(IntermissionMusicDisplay));
        }
    }

    /// <summary>Source ports available for "Compile and run", refreshed from the app
    /// config whenever Project Settings is opened. Shown in the Project Settings.</summary>
    public ObservableCollection<SourcePortConfig> SourcePortOptions { get; } = new();

    /// <summary>Source port chosen for this project (executable path identifies it,
    /// also persisted in the project file). Null = the compile button only builds.</summary>
    public SourcePortConfig? SelectedSourcePort
    {
        get => _selectedSourcePort;
        set => SetProperty(ref _selectedSourcePort, value);
    }

    /// <summary>Reloads <see cref="SourcePortOptions"/> from the app config and selects
    /// the port matching <paramref name="preferredPath"/> (a path saved in a project),
    /// or no port when it no longer exists.</summary>
    public void RefreshSourcePortOptions(string? preferredPath = null)
    {
        SourcePortOptions.Clear();
        foreach (var port in AppSettingsService.Load().SourcePorts)
            SourcePortOptions.Add(port);
        SelectedSourcePort = SourcePortOptions.FirstOrDefault(p =>
            string.Equals(p.ExecutablePath, preferredPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Text shown next to the intermission music field of Project Settings:
    /// the lump name, or the localized "no music" label.</summary>
    public string IntermissionMusicDisplay
    {
        get
        {
            string name = NormalizeMusic(IntermissionMusic) ?? "";
            if (name.Length == 0)
                return LanguageService.GetString("NoMusic");
            return string.IsNullOrWhiteSpace(IntermissionMusicExternalPath)
                ? name
                : $"{name}  {LanguageService.GetString("Music.ExtMark")}";
        }
    }

    /// <summary>Header shown above the maps/slots table reflecting the project name,
    /// version and total slot count.</summary>
    public string MapsHeader
    {
        get
        {
            string name = ProjectName.Trim();
            string v = VersionPrefix.Trim();
            string slots = SlotCount > 0 ? string.Format(LanguageService.GetString("Header.SlotsSuffix"), SlotCount) : "";
            if (name.Length == 0 && v.Length == 0 && slots.Length == 0)
                return LanguageService.GetString("Header.EmptyTitle");
            string suffix = v.Length > 0 ? $"{v}.xxxxxx" : $"v{CompilerInfo.Version}.xxxxxx";
            string body = name.Length > 0 ? $"{name} — {suffix}" : suffix;
            return $"{body}{slots}{LanguageService.GetString("Header.EditableSuffix")}";
        }
    }

    /// <summary>Total number of slots the PWAD will have (planning value shown in the sheet).
    /// When increased, the sheet is padded with empty placeholders; decreasing it never
    /// removes occupied rows so user data is never lost.</summary>
    public int SlotCount
    {
        get => _slotCount;
        set
        {
            if (!SetProperty(ref _slotCount, value)) return;
            OnPropertyChanged(nameof(MapsHeader));
            EnsureSlotPlaceholders();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Index of the sheet row currently highlighted as the drag-and-drop target, or
    /// null when no drag is in progress. Drives the blue highlight of the destination slot.</summary>
    public int? DropTargetIndex
    {
        get => _dropTargetIndex;
        private set
        {
            if (SetProperty(ref _dropTargetIndex, value))
                UpdateDropTargetFlags();
        }
    }

    /// <summary>Sets the row highlighted as the drop target during a drag (null to clear).</summary>
    public void SetDropTargetIndex(int? index) => DropTargetIndex = index is { } i
        ? Math.Clamp(i, 0, Math.Max(0, SlotRows.Count - 1))
        : null;

    private void UpdateDropTargetFlags()
    {
        for (int i = 0; i < SlotRows.Count; i++)
            SlotRows[i].IsDropTarget = i == _dropTargetIndex;
    }

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public WadEntryViewModel? SelectedWad
    {
        get => _selectedWad;
        set => SetProperty(ref _selectedWad, value);
    }

    public WadEntryViewModel? SelectedResourceWad
    {
        get => _selectedResourceWad;
        set => SetProperty(ref _selectedResourceWad, value);
    }

    public IReadOnlyList<string> InputWadPaths => InputWads.Select(w => w.Path).ToList();

    public IReadOnlyList<string> ResourceWadPaths => ResourceWads.Select(w => w.Path).ToList();

    // ------------------------------------------------------------------
    // Input WAD management
    // ------------------------------------------------------------------

    /// <summary>Loads the given paths, parses them and refreshes the map list.</summary>
    public void AddWads(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (InputWads.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                using var wad = WadFile.Open(path);
                var mapInfoData = MapInfoParser.Parse(wad);
                var maps = MapDetector.DetectMaps(wad)
                    .Select(m => new MapOption(
                        m.OriginalName, 
                        m.IsUdmf, 
                        mapInfoData.TryGetValue(m.OriginalName, out var info) ? info.LevelName : null,
                        mapInfoData.TryGetValue(m.OriginalName, out info) ? info.MusicName : null,
                        mapInfoData.TryGetValue(m.OriginalName, out info) ? info.SkyName : null))
                    .ToList();
                InputWads.Add(new WadEntryViewModel
                {
                    Path = path,
                    WadTypeLabel = wad.WadType == WadType.IWad ? "IWAD" : "PWAD",
                    Maps = maps,
                });
            }
            catch (WadException ex)
            {
                AppendLog($"[ERROR] {ex.Message}");
            }
        }

        RefreshMusicOptions();
    }

    public void RemoveSelectedWad()
    {
        if (SelectedWad is null)
            return;
        string removedPath = SelectedWad.Path;
        InputWads.Remove(SelectedWad);
        SelectedWad = null;

        // Free the slots that were fed by maps of the removed WAD.
        for (int i = SlotRows.Count - 1; i >= 0; i--)
        {
            if (!SlotRows[i].IsEmpty && string.Equals(SlotRows[i].WadPath, removedPath, StringComparison.OrdinalIgnoreCase))
                SlotRows[i] = new SlotRowViewModel(null, null, false) { MusicOptions = AvailableMusicLumps };
        }
        RenumberSlotsByPosition();
        RefreshMusicOptions();
    }

    public void MoveSelectedWad(int delta)
    {
        if (SelectedWad is null)
            return;
        int index = InputWads.IndexOf(SelectedWad);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= InputWads.Count)
            return;

        InputWads.Move(index, target);
    }

    // ------------------------------------------------------------------
    // Resource WAD (texture/flat packs) management
    // ------------------------------------------------------------------

    /// <summary>Loads resource WADs (texture/flat packs) into the resources list.</summary>
    public void AddResourceWads(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (ResourceWads.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                using var wad = WadFile.Open(path);
                ResourceWads.Add(new WadEntryViewModel
                {
                    Path = path,
                    WadTypeLabel = wad.WadType == WadType.IWad ? "IWAD" : "PWAD",
                    IsResource = true,
                });
            }
            catch (WadException ex)
            {
                AppendLog($"[ERROR] {ex.Message}");
            }
        }
        RefreshMusicOptions();
    }

    public void RemoveSelectedResourceWad()
    {
        if (SelectedResourceWad is null)
            return;
        ResourceWads.Remove(SelectedResourceWad);
        SelectedResourceWad = null;
        RefreshMusicOptions();
    }

    public void MoveSelectedResourceWad(int delta)
    {
        if (SelectedResourceWad is null)
            return;
        int index = ResourceWads.IndexOf(SelectedResourceWad);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= ResourceWads.Count)
            return;
        ResourceWads.Move(index, target);
    }

    public void Clear()
    {
        InputWads.Clear();
        ResourceWads.Clear();
        SlotRows.Clear();
        AvailableMusicLumps.Clear();
        Collaborators.Clear();
        BaseWadPath = null;
        OutputPath = null;
        AutoAssignMaps = true;
        FilterResourcesToUsed = true;
        ProjectName = "";
        VersionPrefix = "";
        SlotCount = 32;
        CurrentProjectPath = null;
        IntermissionMusic = "";
        IntermissionMusicExternalPath = "";
        LogEntries.Clear();
        RebuildSlots();
    }

    /// <summary>Rebuilds the collaborators list from the unique authors of the occupied slots,
    /// keeping any manually-added collaborator that is not among the authors.</summary>
    public void SyncCollaboratorsFromAuthors()
    {
        var authors = new List<string>();
        var seenAuthors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in SlotRows)
        {
            if (row.IsEmpty)
                continue;
            string name = row.Author.Trim();
            if (name.Length > 0 && seenAuthors.Add(name))
                authors.Add(name);
        }

        var extra = Collaborators
            .Select(c => c.Name.Trim())
            .Where(n => n.Length > 0 && !seenAuthors.Contains(n))
            .ToList();

        Collaborators.Clear();
        foreach (string name in authors)
            Collaborators.Add(new CollaboratorEntryViewModel(name));
        foreach (string name in extra)
            Collaborators.Add(new CollaboratorEntryViewModel(name));
    }

    /// <summary>Pads the sheet with empty placeholder rows until it has <see cref="SlotCount"/> rows.</summary>
    private void EnsureSlotPlaceholders()
    {
        while (SlotRows.Count < SlotCount)
        {
            var placeholder = new SlotRowViewModel(null, null, false) { MusicOptions = AvailableMusicLumps };
            SlotRows.Add(placeholder);
        }
        RenumberSlotsByPosition();
    }

    /// <summary>
    /// Rebuilds the sheet as a plan of <see cref="SlotCount"/> slots: maps are no longer
    /// auto-assigned (they must be dragged from the input WADs into a slot). Rows referencing
    /// WADs that were removed are cleared, the sheet is padded with empty slots and all slot
    /// names are renumbered by position (MAP01...).
    /// </summary>
    public void RebuildSlots()
    {
        RefreshMusicOptions();

        // Clear slots fed by WADs that are no longer in the inputs list.
        var validPaths = new HashSet<string>(InputWads.Select(w => w.Path), StringComparer.OrdinalIgnoreCase);
        for (int i = SlotRows.Count - 1; i >= 0; i--)
        {
            if (!SlotRows[i].IsEmpty && !validPaths.Contains(SlotRows[i].WadPath!))
                SlotRows[i] = new SlotRowViewModel(null, null, false) { MusicOptions = AvailableMusicLumps };
        }

        // Pad with empty placeholder slots up to the configured slot count.
        while (SlotRows.Count < SlotCount)
            SlotRows.Add(new SlotRowViewModel(null, null, false) { MusicOptions = AvailableMusicLumps });

        RenumberSlotsByPosition();
    }

    /// <summary>Clears every field of the slot at <paramref name="index"/>, returning it to
    /// an empty placeholder (no map assigned, no name/música/autor/estado). Used by the
    /// row context-menu "Borrar todos los campos".</summary>
    public void ClearSlot(int index)
    {
        if (index < 0 || index >= SlotRows.Count)
            return;

        bool hadMap = !SlotRows[index].IsEmpty;
        SlotRows[index] = new SlotRowViewModel(null, null, false) { MusicOptions = AvailableMusicLumps };
        RenumberSlotsByPosition();
        if (hadMap)
            AppendLog(string.Format(LanguageService.GetString("Log.SlotCleared"), SlotRows[index].SlotName));
    }

    /// <summary>Assigns a map (from an input WAD) to the slot at <paramref name="index"/>.
    /// Used when a map is dragged and dropped onto a slot row of the sheet.</summary>
    public void AssignMapToSlot(int index, WadEntryViewModel source, MapOption map)
    {
        if (index < 0 || index >= SlotRows.Count)
            return;

        bool replaced = !SlotRows[index].IsEmpty;
        SlotRows[index] = new SlotRowViewModel(source.Path, map.OriginalName, map.IsUdmf)
        {
            MusicOptions = AvailableMusicLumps,
            LevelName = map.LevelName ?? "",
            MusicName = map.MusicName ?? "",
            SkyName = map.SkyName ?? "sky1", // default sky1 if not specified
        };
        RenumberSlotsByPosition();
        string slot = SlotRows[index].SlotName;
        AppendLog(replaced
            ? string.Format(LanguageService.GetString("Log.MapReplaced"), map.OriginalName, source.FileName, slot)
            : string.Format(LanguageService.GetString("Log.MapAssigned"), map.OriginalName, source.FileName, slot));
    }

    /// <summary>Renumbers every slot name by its row position (MAP01, MAP02, ...).
    /// Used after a drag & drop so the dropped row takes the slot of its new position.</summary>
    public void RenumberSlotsByPosition()
    {
        for (int i = 0; i < SlotRows.Count; i++)
            SlotRows[i].SlotName = $"MAP{i + 1:D2}";
    }

    /// <summary>Clears all slot rows whose Author matches <paramref name="author"/>
    /// (case-insensitive). Used when a collaborator is removed from the project.</summary>
    public void RemoveMapsByAuthor(string author)
    {
        if (string.IsNullOrWhiteSpace(author))
            return;

        bool any = false;
        for (int i = 0; i < SlotRows.Count; i++)
        {
            if (!SlotRows[i].IsEmpty &&
                string.Equals(SlotRows[i].Author?.Trim(), author.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                SlotRows[i] = new SlotRowViewModel(null, null, false) { MusicOptions = AvailableMusicLumps };
                any = true;
            }
        }
        if (any)
        {
            RenumberSlotsByPosition();
            AppendLog(string.Format(LanguageService.GetString("Log.MapsByAuthorRemoved"), author));
        }
    }

    /// <summary>Re-scans all input/resource WADs for music lumps (MUS/MIDI signatures),
    /// renaming duplicate names (same lump in two WADs) to <c>NOMBRE_WAD</c>.</summary>
    private void RefreshMusicOptions()
    {
        var opened = new List<WadFile>();
        try
        {
            foreach (string path in InputWads.Select(w => w.Path).Concat(ResourceWads.Select(w => w.Path)))
            {
                try
                {
                    opened.Add(WadFile.Open(path));
                }
                catch (WadException ex)
                {
                    AppendLog($"[ERROR] {ex.Message}");
                }
            }

            var wanted = new List<MusicLumpInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var renames = new List<string>();
            foreach (var c in MusicLumpDetector.CollectAcrossWads(opened))
            {
                if (!string.Equals(c.FinalName, c.OriginalName, StringComparison.Ordinal))
                    renames.Add($"'{c.OriginalName}' de '{Path.GetFileName(c.WadPath)}' → '{c.FinalName}'");
                if (seen.Add(c.FinalName))
                    wanted.Add(new MusicLumpInfo(c.FinalName, c.WadPath));
            }

            AvailableMusicLumps.Clear();
            foreach (var info in wanted)
                AvailableMusicLumps.Add(info);
// "No music" is always available as the last option.
            AvailableMusicLumps.Add(new MusicLumpInfo(SlotRowViewModel.NoMusicOption, ""));


            if (renames.Count > 0)
                AppendLog(string.Format(LanguageService.GetString("Log.MusicRenamed"), string.Join("; ", renames)));
        }
        finally
        {
            foreach (var w in opened)
                w.Dispose();
        }
    }

    /// <summary>Attempts to find a texture lump by name in resource WADs and return its raw bytes.
    /// Returns null if not found or if it's a composite texture (TEXTURE1/2).</summary>
    public byte[]? TryGetTextureData(string name)
    {
        foreach (var wadEntry in ResourceWads)
        {
            try
            {
                using var wad = WadFile.Open(wadEntry.Path);
                var lump = wad.FindFirst(name);
                if (lump is not null)
                {
                    // Skip composite texture lumps
                    if (lump.Name.Equals("TEXTURE1", StringComparison.OrdinalIgnoreCase) ||
                        lump.Name.Equals("TEXTURE2", StringComparison.OrdinalIgnoreCase) ||
                        lump.Name.Equals("TEXTURES", StringComparison.OrdinalIgnoreCase) ||
                        lump.Name.Equals("PNAMES", StringComparison.OrdinalIgnoreCase))
                        continue;
                    return lump.ReadAll();
                }
            }
            catch (WadException)
            {
                // Ignore and try next WAD
            }
        }
        return null;
    }

    /// <summary>Finds the first PLAYPAL lump (256 RGB entries) in the resource WADs so the
    /// texture preview can use the palette the user actually plays with. Returns null when
    /// no resource WAD provides a PLAYPAL; callers fall back to the standard Doom palette.</summary>
    public byte[]? TryGetPalette()
    {
        foreach (var wadEntry in ResourceWads)
        {
            try
            {
                using var wad = WadFile.Open(wadEntry.Path);
                var lump = wad.FindFirst("PLAYPAL");
                if (lump is not null && lump.Size == 768)
                    return lump.ReadAll();
            }
            catch (WadException)
            {
                // Ignore and try next WAD
            }
        }
        return null;
    }

    /// <summary>Collects all texture lump names from resource WADs that could be used as sky.
    /// Only texture lumps (patches and standalone texture graphics) are included: sprite
    /// (S_START..S_END), flat (F_START..F_END) and map lumps are filtered out.</summary>
    public List<string> GetSkyTextureNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var opened = new List<WadFile>();
        try
        {
            foreach (string path in ResourceWads.Select(w => w.Path))
            {
                try
                {
                    opened.Add(WadFile.Open(path));
                }
                catch (WadException ex)
                {
                    AppendLog($"[ERROR] {ex.Message}");
                }
            }

            // Texture lump names to exclude (same as in WadMerger)
            var textureLumpNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "PNAMES", "TEXTURE1", "TEXTURE2", "TEXTURES",
            };

            foreach (var wad in opened)
            {
                var categories = LumpGroupClassifier.ClassifyAll(wad.Lumps);
                var mapRanges = MapDetector.DetectMaps(wad)
                    .Select(m => (m.StartIndex, m.EndIndex))
                    .ToList();

                for (int i = 0; i < wad.Lumps.Count; i++)
                {
                    var lump = wad.Lumps[i];

                    // Only texture lumps: skip sprites, flats, the group markers and maps.
                    if (categories[i] is LumpCategory.Sprite or LumpCategory.Flat ||
                        LumpGroupClassifier.IsMarker(lump.Name) ||
                        mapRanges.Any(r => i >= r.StartIndex && i < r.EndIndex))
                        continue;

                    // Add patch names (from PNAMES/TEXTURE1) and flat names
                    // We include all non-map lumps that could be sky textures
                    if (!textureLumpNames.Contains(lump.Name) &&
                        !MapDetector.IsMapHeader(lump.Name) &&
                        lump.Name.Length <= 8)
                    {
                        names.Add(lump.Name);
                    }
                }
            }
        }
        finally
        {
            foreach (var w in opened)
                w.Dispose();
        }

        var result = names.OrderBy(n => n).ToList();
        // Ensure sky1 is always present as default
        if (!result.Contains("sky1", StringComparer.OrdinalIgnoreCase))
            result.Insert(0, "sky1");
        return result;
    }

    /// <summary>Maps the selected music value to a lump name; the "no music" sentinel
    /// (either language) or an empty value becomes <c>null</c>.</summary>
    private static string? NormalizeMusic(string? value)
    {
        string v = value?.Trim() ?? "";
        if (v.Length == 0)
            return null;
        if (v == SlotRowViewModel.NoMusicOption ||
            v == LanguageService.GetString("NoMusic") ||
            v == "Ninguna" || v == "None")
            return null;
        return v;
    }

    // ------------------------------------------------------------------
    // Project (de)serialization
    // ------------------------------------------------------------------

    public void SaveProject(string path)
    {
        var data = new ProjectFileData
        {
            ProjectName = ProjectName,
            VersionPrefix = VersionPrefix,
            MapSlots = Math.Max(1, SlotCount),
            Collaborators = Collaborators.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
            BaseWadPath = BaseWadPath,
            OutputPath = OutputPath,
            WadPaths = InputWads.Select(w => w.Path).ToList(),
            ResourceWadPaths = ResourceWads.Select(w => w.Path).ToList(),
            AutoAssignMaps = AutoAssignMaps,
            FilterResourcesToUsed = FilterResourcesToUsed,
            IncludePaletteLumps = IncludePaletteLumps,
            IncludeSpriteLumps = IncludeSpriteLumps,
            IntermissionMusic = IntermissionMusic,
            IntermissionMusicExternalPath = IntermissionMusicExternalPath,
            SourcePortPath = SelectedSourcePort?.ExecutablePath,
            Maps = SlotRows
                .Where(r => !r.IsEmpty)
                .Select(r => new MapEntryData
                {
                    WadPath = r.WadPath!,
                    OriginalName = r.OriginalName!,
                    FinalName = r.SlotName,
                    IsUdmf = r.IsUdmf,
                    LevelName = r.LevelName,
                    MusicName = r.MusicName,
                    MusicExternalPath = r.MusicExternalPath,
                    SkyName = r.SkyName,
                    Sky2Name = r.Sky2Name,
                    SkyScroll = r.SkyScroll,
                    Sky2Scroll = r.Sky2Scroll,
                    EnableSky2 = r.EnableSky2,
                    Author = r.Author,
                    Status = r.Status,
                    Notes = r.Notes,
                })
                .ToList(),
        };

        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        CurrentProjectPath = path;
        AppendLog(string.Format(LanguageService.GetString("Log.ProjectSaved"), path));
    }

    public void LoadProject(string path)
    {
        string json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<ProjectFileData>(json);
        if (data is null)
        {
            AppendLog(string.Format(LanguageService.GetString("Log.ProjectReadError"), path));
            return;
        }

        Clear();
        ProjectName = data.ProjectName ?? "";
        VersionPrefix = data.VersionPrefix ?? "";
        SlotCount = Math.Max(1, data.MapSlots);
        foreach (string name in data.Collaborators)
            if (!string.IsNullOrWhiteSpace(name))
                Collaborators.Add(new CollaboratorEntryViewModel(name));
        BaseWadPath = data.BaseWadPath;
        OutputPath = data.OutputPath;
        AutoAssignMaps = data.AutoAssignMaps;
        FilterResourcesToUsed = data.FilterResourcesToUsed;
        IncludePaletteLumps = data.IncludePaletteLumps;
        IncludeSpriteLumps = data.IncludeSpriteLumps;
        IntermissionMusic = data.IntermissionMusic ?? "";
        IntermissionMusicExternalPath = data.IntermissionMusicExternalPath ?? "";
        RefreshSourcePortOptions(data.SourcePortPath);

        foreach (string wadPath in data.WadPaths)
            AddWadsPathOnly(wadPath);

        foreach (string wadPath in data.ResourceWadPaths)
            AddResourceWadsPathOnly(wadPath);

        RefreshMusicOptions();

        // Place the persisted slot rows at their positions, then pad with empty
        // placeholder rows up to the project slot count; RebuildSlots re-fits any
        // maps that map identity now assigns differently.
        SlotRows.Clear();
        foreach (var m in data.Maps)
        {
            SlotRows.Add(new SlotRowViewModel(m.WadPath, m.OriginalName, m.IsUdmf)
            {
                SlotName = m.FinalName,
                LevelName = m.LevelName ?? "",
                MusicName = m.MusicName ?? "",
                MusicExternalPath = m.MusicExternalPath ?? "",
                SkyName = m.SkyName ?? "sky1",
                Sky2Name = m.Sky2Name ?? "",
                SkyScroll = m.SkyScroll,
                Sky2Scroll = m.Sky2Scroll,
                EnableSky2 = m.EnableSky2,
                Author = m.Author ?? "",
                Status = m.Status ?? "",
                Notes = m.Notes ?? "",
                MusicOptions = AvailableMusicLumps,
            });
        }

        for (int i = data.Maps.Count; i < SlotCount; i++)
        {
            SlotRows.Add(new SlotRowViewModel(null, null, false)
            {
                SlotName = AutoAssignMaps ? $"MAP{i + 1:D2}" : "",
                MusicOptions = AvailableMusicLumps,
            });
        }

        RebuildSlots();

        AppendLog(string.Format(LanguageService.GetString("Log.ProjectLoaded"), path));
        CurrentProjectPath = path;
    }

    private void AddWadsPathOnly(string path)
    {
        if (!File.Exists(path) || InputWads.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            using var wad = WadFile.Open(path);
            var mapInfoData = MapInfoParser.Parse(wad);
            var maps = MapDetector.DetectMaps(wad)
                .Select(m => new MapOption(
                    m.OriginalName, 
                    m.IsUdmf, 
                    mapInfoData.TryGetValue(m.OriginalName, out var info) ? info.LevelName : null,
                    mapInfoData.TryGetValue(m.OriginalName, out info) ? info.MusicName : null,
                    mapInfoData.TryGetValue(m.OriginalName, out info) ? info.SkyName : null))
                .ToList();
            InputWads.Add(new WadEntryViewModel
            {
                Path = path,
                WadTypeLabel = wad.WadType == WadType.IWad ? "IWAD" : "PWAD",
                Maps = maps,
            });
        }
        catch (WadException ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
        }
    }

    private void AddResourceWadsPathOnly(string path)
    {
        if (!File.Exists(path) || ResourceWads.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            using var wad = WadFile.Open(path);
            ResourceWads.Add(new WadEntryViewModel
            {
                Path = path,
                WadTypeLabel = wad.WadType == WadType.IWad ? "IWAD" : "PWAD",
                IsResource = true,
            });
        }
        catch (WadException ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Compile
    // ------------------------------------------------------------------

    public async Task CompileAsync(bool runAfterBuild = false)
    {
        if (IsBusy)
            return;

        if (InputWads.Count == 0)
        {
            AppendLog(LanguageService.GetString("Log.NeedInputWad"));
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            AppendLog(LanguageService.GetString("Log.NeedOutputPath"));
            return;
        }

        var assignments = new List<MapAssignment>(SlotRows.Count);
        foreach (var row in SlotRows)
        {
            if (row.IsEmpty)
                continue;

            string final = row.SlotName.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(final))
            {
                AppendLog(string.Format(LanguageService.GetString("Log.NoSlot"), row.OriginalName, row.WadPath));
                return;
            }
            if (!string.Equals(final, row.SlotName, StringComparison.OrdinalIgnoreCase))
                row.SlotName = final;

            string? music = NormalizeMusic(row.MusicName);
            string? sky = string.IsNullOrWhiteSpace(row.SkyName) ? null : row.SkyName.Trim();
            string? sky2 = row.EnableSky2 && !string.IsNullOrWhiteSpace(row.Sky2Name)
                ? row.Sky2Name.Trim()
                : null;
            string? author = string.IsNullOrWhiteSpace(row.Author) ? null : row.Author.Trim();
            string? status = string.IsNullOrWhiteSpace(row.Status) ? null : row.Status.Trim();
            string? lastModified = string.IsNullOrWhiteSpace(row.LastModified) ? null : row.LastModified.Trim();
            assignments.Add(new MapAssignment(row.WadPath!, row.OriginalName!, final, row.LevelName,
                music, sky, author, status, lastModified, sky2, row.SkyScroll, row.Sky2Scroll));
        }

        var externalMusic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in SlotRows.Where(r => !r.IsEmpty && !string.IsNullOrWhiteSpace(r.MusicExternalPath) && !string.IsNullOrWhiteSpace(r.MusicName)))
        {
            string lumpName = NormalizeMusic(row.MusicName) ?? "";
            if (lumpName.Length > 0 && !externalMusic.ContainsKey(lumpName))
                externalMusic[lumpName] = row.MusicExternalPath!;
        }

        string? intermission = NormalizeMusic(IntermissionMusic);
        if (intermission is not null
            && !string.IsNullOrWhiteSpace(IntermissionMusicExternalPath)
            && !externalMusic.ContainsKey(intermission))
            externalMusic[intermission] = IntermissionMusicExternalPath;

        var request = new MergeRequest
        {
            ProjectName = string.IsNullOrWhiteSpace(ProjectName) ? null : ProjectName.Trim(),
            VersionPrefix = string.IsNullOrWhiteSpace(VersionPrefix) ? null : VersionPrefix.Trim(),
            BaseWadPath = BaseWadPath,
            InputWadPaths = InputWadPaths,
            ResourceWadPaths = ResourceWadPaths,
            OutputPath = OutputPath,
            MapAssignments = assignments,
            ExternalMusicFiles = externalMusic.Count > 0 ? externalMusic : null,
            IntermissionMusic = intermission,
            Options = new MergeOptions
            {
                AutoAssignMaps = false,
                FilterToUsedResources = FilterResourcesToUsed,
                IncludePaletteLumps = IncludePaletteLumps,
                IncludeSpriteLumps = IncludeSpriteLumps,
            },
        };

        IsBusy = true;
        AppendLog(LanguageService.GetString("Log.Compiling"));

        var progress = new Progress<string>(AppendLog);
        try
        {
            var result = await Task.Run(() => new WadMerger().Merge(request, progress));
            AppendLog(BuildReport(result));
            if (runAfterBuild && result.Success)
                LaunchSourcePort();
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Launches the source port selected for this project with the fresh
    /// output WAD after a successful "Compile and run". The port config (including
    /// its custom arguments) is re-read from the app config, so edits made in
    /// Configuration → Source ports are honored without reopening this project.</summary>
    private void LaunchSourcePort()
    {
        var selected = SelectedSourcePort;
        if (selected is null)
        {
            AppendLog(LanguageService.GetString("Log.NoSourcePort"));
            return;
        }

        // Prefer the current config entry: it may have newer arguments than the
        // options snapshot this project was opened with.
        var config = AppSettingsService.Load().SourcePorts;
        var port = selected;
        if (config.FirstOrDefault(p =>
                string.Equals(p.ExecutablePath, selected.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                || (selected.Name.Length > 0 && string.Equals(p.Name, selected.Name, StringComparison.OrdinalIgnoreCase))) is { } fresh)
            port = fresh;

        if (!File.Exists(port.ExecutablePath))
        {
            AppendLog(string.Format(LanguageService.GetString("Log.SourcePortMissing"), port.ExecutablePath));
            return;
        }

        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(port.Arguments))
            args.Add(port.Arguments.Trim());
        if (BaseWadPath is { } baseWad && File.Exists(baseWad))
            args.Add($"-iwad \"{baseWad}\"");
        args.Add($"-file \"{OutputPath}\"");

        var psi = new ProcessStartInfo
        {
            FileName = port.ExecutablePath,
            Arguments = string.Join(" ", args),
            WorkingDirectory = Path.GetDirectoryName(port.ExecutablePath) ?? "",
            UseShellExecute = false,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is not null)
                AppendLog(string.Format(LanguageService.GetString("Log.SourcePortLaunched"), port.Name, process.Id));
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Log
    // ------------------------------------------------------------------

    /// <summary>Builds the localized compile report from a merge result.</summary>
    private string BuildReport(MergeResult result)
    {
        var sb = new StringBuilder();
        string elapsed = result.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
        sb.AppendLine(result.Success
            ? string.Format(LanguageService.GetString("Report.Success"), elapsed)
            : LanguageService.GetString("Report.Failed"));
        if (result.OutputPath is not null)
            sb.AppendLine(string.Format(LanguageService.GetString("Report.Output"),
                result.OutputPath, result.OutputBytes.ToString("N0", CultureInfo.InvariantCulture)));
        sb.AppendLine(string.Format(LanguageService.GetString("Report.Maps"), result.MapsAdded));
        sb.AppendLine(string.Format(LanguageService.GetString("Report.Lumps"), result.LumpsCopied, result.DuplicatesSkipped));
        sb.AppendLine(string.Format(LanguageService.GetString("Report.Patches"), result.PatchesMerged, result.TexturesMerged, result.TexturesDuplicated));
        if (result.TexturesExcluded > 0)
            sb.AppendLine(string.Format(LanguageService.GetString("Report.TexturesExcluded"), result.TexturesExcluded));
        if (result.FlatsCopied > 0)
            sb.AppendLine(string.Format(LanguageService.GetString("Report.Flats"), result.FlatsCopied));
        if (result.MusicCopied > 0)
            sb.AppendLine(string.Format(LanguageService.GetString("Report.Music"), result.MusicCopied));
        foreach (string e in result.Errors)
            sb.AppendLine(string.Format(LanguageService.GetString("Log.ReportError"), e));
        foreach (string w in result.Warnings)
            sb.AppendLine(string.Format(LanguageService.GetString("Log.ReportWarning"), w));
        foreach (string i in result.Info)
            sb.AppendLine(string.Format(LanguageService.GetString("Log.ReportInfo"), i));
        return sb.ToString();
    }

    public void AppendLog(string line)
    {
        if (LogEntries.Count > 0)
            LogEntries.Add(LogEntry.Separator);
        foreach (var part in line.Split('\n'))
        {
            var text = part.TrimEnd('\r');
            if (text.Length == 0)
                continue;
            LogEntries.Add(LogEntry.Create(text));
        }
    }
}