using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CommunityWadCompiler.App.Models;
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
    private string _logText = "";
    private bool _isBusy;
    private bool _autoAssignMaps = true;
    private bool _filterResourcesToUsed = true;
    private WadEntryViewModel? _selectedWad;
    private WadEntryViewModel? _selectedResourceWad;
    private string _projectName = "";
    private string _versionPrefix = "";
    private int _slotCount = 32;
    private string? _currentProjectPath;

    public ObservableCollection<WadEntryViewModel> InputWads { get; } = new();

    public ObservableCollection<WadEntryViewModel> ResourceWads { get; } = new();

    /// <summary>Slot rows of the plan sheet: one row per slot (empty slots included).</summary>
    public ObservableCollection<SlotRowViewModel> SlotRows { get; } = new();

    public ObservableCollection<string> AvailableMusicLumps { get; } = new();

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

    /// <summary>Header shown above the maps/slots table reflecting the project name,
    /// version and total slot count.</summary>
    public string MapsHeader
    {
        get
        {
            string name = ProjectName.Trim();
            string v = VersionPrefix.Trim();
            string slots = SlotCount > 0 ? $" · {SlotCount} slots" : "";
            if (name.Length == 0 && v.Length == 0 && slots.Length == 0)
                return "Mapas y slots finales (editable)";
            string suffix = v.Length > 0 ? $"{v}.xxxxxx" : $"v{CompilerInfo.Version}.xxxxxx";
            string body = name.Length > 0 ? $"{name} — {suffix}" : suffix;
            return $"{body}{slots} (editable)";
        }
    }

    /// <summary>Total number of slots the PWAD will have (planning value shown in the sheet).</summary>
    public int SlotCount
    {
        get => _slotCount;
        set { if (SetProperty(ref _slotCount, value)) OnPropertyChanged(nameof(MapsHeader)); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

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
                int maps = MapDetector.DetectMaps(wad).Count;
                InputWads.Add(new WadEntryViewModel
                {
                    Path = path,
                    WadTypeLabel = wad.WadType == WadType.IWad ? "IWAD" : "PWAD",
                    MapCount = maps,
                });
            }
            catch (WadException ex)
            {
                AppendLog($"[ERROR] {ex.Message}");
            }
        }

        RebuildSlots();
    }

    public void RemoveSelectedWad()
    {
        if (SelectedWad is null)
            return;
        InputWads.Remove(SelectedWad);
        SelectedWad = null;
        RebuildSlots();
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
        RebuildSlots();
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
                    Kind = "recursos",
                });
            }
            catch (WadException ex)
            {
                AppendLog($"[ERROR] {ex.Message}");
            }
        }
    }

    public void RemoveSelectedResourceWad()
    {
        if (SelectedResourceWad is null)
            return;
        ResourceWads.Remove(SelectedResourceWad);
        SelectedResourceWad = null;
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
        LogText = "";
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

    /// <summary>
    /// Regenerates the slot rows from the inputs: one row per slot (empty slots are kept as
    /// placeholders). Maps that were already in a slot keep their row/fields; new maps fill
    /// the first free slots in order. Empty slot names are auto-assigned by position (MAP01...).
    /// </summary>
    public void RebuildSlots()
    {
        // Keep rows of previously assigned maps (identity = source WAD + lump) so their
        // edited slot names, level names, authors, etc. survive reordering.
        var existing = new Dictionary<(string, string), SlotRowViewModel>();
        foreach (var row in SlotRows)
            if (!row.IsEmpty)
                existing[(row.WadPath!, row.OriginalName!)] = row;

        var maps = new List<(string Path, string Original, bool IsUdmf)>();
        foreach (var wadEntry in InputWads)
        {
            try
            {
                using var wad = WadFile.Open(wadEntry.Path);
                foreach (var map in MapDetector.DetectMaps(wad))
                    maps.Add((wadEntry.Path, map.OriginalName, map.IsUdmf));
            }
            catch (WadException ex)
            {
                AppendLog($"[ERROR] {ex.Message}");
            }
        }

        // Preserve the manual order of the sheet: maps currently in a row stay in the
        // order the user left them (drag & drop); newly added maps are appended.
        var order = new List<(string, string)>();
        foreach (var row in SlotRows)
            if (!row.IsEmpty)
                order.Add((row.WadPath!, row.OriginalName!));

        var byKey = maps.ToDictionary(m => (m.Path, m.Original), m => m);
        var ordered = new List<(string Path, string Original, bool IsUdmf)>();
        var placed = new HashSet<(string, string)>();
        foreach (var key in order)
            if (byKey.TryGetValue(key, out var m))
            {
                ordered.Add(m);
                placed.Add(key);
            }
        foreach (var m in maps)
            if (placed.Add((m.Path, m.Original)))
                ordered.Add(m);

        RefreshMusicOptions();
        SlotRows.Clear();
        int count = Math.Max(SlotCount, ordered.Count);

        for (int i = 0; i < count; i++)
        {
            SlotRowViewModel row;
            if (i < ordered.Count)
            {
                var m = ordered[i];
                row = existing.TryGetValue((m.Path, m.Original), out var prior)
                    ? prior
                    : new SlotRowViewModel(m.Path, m.Original, m.IsUdmf);
            }
            else
            {
                row = new SlotRowViewModel(null, null, false);
            }

            if (AutoAssignMaps && string.IsNullOrWhiteSpace(row.SlotName))
                row.SlotName = $"MAP{i + 1:D2}";
            row.MusicOptions = AvailableMusicLumps;
            SlotRows.Add(row);
        }
    }

    /// <summary>Renumbers every slot name by its row position (MAP01, MAP02, ...).
    /// Used after a drag & drop so the dropped row takes the slot of its new position.</summary>
    public void RenumberSlotsByPosition()
    {
        for (int i = 0; i < SlotRows.Count; i++)
            SlotRows[i].SlotName = $"MAP{i + 1:D2}";
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

            var wanted = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var renames = new List<string>();
            foreach (var c in MusicLumpDetector.CollectAcrossWads(opened))
            {
                if (!string.Equals(c.FinalName, c.OriginalName, StringComparison.Ordinal))
                    renames.Add($"'{c.OriginalName}' de '{Path.GetFileName(c.WadPath)}' → '{c.FinalName}'");
                if (seen.Add(c.FinalName))
                    wanted.Add(c.FinalName);
            }

            AvailableMusicLumps.Clear();
            foreach (string name in wanted)
                AvailableMusicLumps.Add(name);

            // "No music" is always available as the last option.
            AvailableMusicLumps.Add(SlotRowViewModel.NoMusicOption);

            if (renames.Count > 0)
                AppendLog($"[INFO] Música renombrada por nombre duplicado: {string.Join("; ", renames)}");
        }
        finally
        {
            foreach (var w in opened)
                w.Dispose();
        }
    }

    /// <summary>Maps the selected music value to a lump name; the "no music" sentinel
    /// (or an empty value) becomes <c>null</c>.</summary>
    private static string? NormalizeMusic(string? value)
    {
        string v = value?.Trim() ?? "";
        return v.Length == 0 || v == SlotRowViewModel.NoMusicOption ? null : v;
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
                    Author = r.Author,
                    Status = r.Status,
                })
                .ToList(),
        };

        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        CurrentProjectPath = path;
        AppendLog($"[INFO] Proyecto guardado en {path}");
    }

    public void LoadProject(string path)
    {
        string json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<ProjectFileData>(json);
        if (data is null)
        {
            AppendLog($"[ERROR] No se pudo leer el proyecto {path}");
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
                Author = m.Author ?? "",
                Status = m.Status ?? "",
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

        AppendLog($"[INFO] Proyecto cargado desde {path}");
        CurrentProjectPath = path;
    }

    private void AddWadsPathOnly(string path)
    {
        if (!File.Exists(path) || InputWads.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            using var wad = WadFile.Open(path);
            InputWads.Add(new WadEntryViewModel
            {
                Path = path,
                WadTypeLabel = wad.WadType == WadType.IWad ? "IWAD" : "PWAD",
                MapCount = MapDetector.DetectMaps(wad).Count,
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
                Kind = "recursos",
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

    public async Task CompileAsync()
    {
        if (IsBusy)
            return;

        if (InputWads.Count == 0)
        {
            AppendLog("[ERROR] Agregá al menos un WAD de entrada.");
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            AppendLog("[ERROR] Definí la ruta de salida.");
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
                AppendLog($"[ERROR] El mapa '{row.OriginalName}' de '{row.WadPath}' no tiene slot final.");
                return;
            }
            if (!string.Equals(final, row.SlotName, StringComparison.OrdinalIgnoreCase))
                row.SlotName = final;

            string? music = NormalizeMusic(row.MusicName);
            string? author = string.IsNullOrWhiteSpace(row.Author) ? null : row.Author.Trim();
            string? status = string.IsNullOrWhiteSpace(row.Status) ? null : row.Status.Trim();
            string? lastModified = string.IsNullOrWhiteSpace(row.LastModified) ? null : row.LastModified.Trim();
            assignments.Add(new MapAssignment(row.WadPath!, row.OriginalName!, final, row.LevelName, music, author, status, lastModified));
        }

        var request = new MergeRequest
        {
            ProjectName = string.IsNullOrWhiteSpace(ProjectName) ? null : ProjectName.Trim(),
            VersionPrefix = string.IsNullOrWhiteSpace(VersionPrefix) ? null : VersionPrefix.Trim(),
            BaseWadPath = BaseWadPath,
            InputWadPaths = InputWadPaths,
            ResourceWadPaths = ResourceWadPaths,
            OutputPath = OutputPath,
            MapAssignments = assignments,
            Options = new MergeOptions
            {
                AutoAssignMaps = false,
                FilterToUsedResources = FilterResourcesToUsed,
            },
        };

        IsBusy = true;
        AppendLog("── Compilando ───────────────────────────────");

        var progress = new Progress<string>(AppendLog);
        try
        {
            var result = await Task.Run(() => new WadMerger().Merge(request, progress));
            AppendLog(result.ToReport());
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

    // ------------------------------------------------------------------
    // Log
    // ------------------------------------------------------------------

    public void AppendLog(string line)
    {
        var sb = new StringBuilder(LogText);
        if (sb.Length > 0)
            sb.AppendLine();
        sb.Append(line);
        LogText = sb.ToString();
    }
}