using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CommunityWadCompiler.App.Models;
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
    private readonly HashSet<(string Wad, string Original)> _userEditedSlots = new();

    private string? _baseWadPath;
    private string? _outputPath;
    private string _logText = "";
    private bool _isBusy;
    private bool _autoAssignMaps = true;
    private bool _filterResourcesToUsed = true;
    private WadEntryViewModel? _selectedWad;
    private WadEntryViewModel? _selectedResourceWad;

    public ObservableCollection<WadEntryViewModel> InputWads { get; } = new();

    public ObservableCollection<WadEntryViewModel> ResourceWads { get; } = new();

    public ObservableCollection<MapEntryViewModel> Maps { get; } = new();

    public ObservableCollection<string> AvailableMusicLumps { get; } = new();

    public string? BaseWadPath
    {
        get => _baseWadPath;
        set => SetProperty(ref _baseWadPath, value);
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

        RebuildMaps();
    }

    public void RemoveSelectedWad()
    {
        if (SelectedWad is null)
            return;
        InputWads.Remove(SelectedWad);
        SelectedWad = null;
        RebuildMaps();
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
        RebuildMaps();
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
        Maps.Clear();
        AvailableMusicLumps.Clear();
        _userEditedSlots.Clear();
        BaseWadPath = null;
        OutputPath = null;
        AutoAssignMaps = true;
        FilterResourcesToUsed = true;
        LogText = "";
    }

    /// <summary>
    /// Regenerates the map list from the inputs, preserving slots the user edited and
    /// auto-assigning fresh sequential slots to everything else.
    /// </summary>
    public void RebuildMaps()
    {
        // Preserve existing final names for maps that were not touched by the user.
        var existing = Maps.ToDictionary(
            m => (m.WadPath, m.OriginalName),
            m => (m.FinalName, m.LevelName, m.MusicName, m.Author));

        RefreshMusicOptions();
        Maps.Clear();
        int autoCounter = 1;
        string currentPrefix = AutoAssignMaps ? "MAP" : "";

        foreach (var wadEntry in InputWads)
        {
            try
            {
                using var wad = WadFile.Open(wadEntry.Path);
                foreach (var map in MapDetector.DetectMaps(wad))
                {
                    string finalName;
                    string? autoName = null;
                    var key = (wadEntry.Path, map.OriginalName);

                    if (_userEditedSlots.Contains(key) && existing.TryGetValue(key, out var kept))
                    {
                        finalName = kept.FinalName;
                    }
                    else if (AutoAssignMaps)
                    {
                        finalName = $"{currentPrefix}{autoCounter++:D2}";
                        autoName = finalName;
                    }
                    else
                    {
                        finalName = "";
                    }

                    existing.TryGetValue(key, out var prior);
                    var entry = new MapEntryViewModel(wadEntry.Path, map.OriginalName, finalName, map.IsUdmf)
                    {
                        AutoAssignedName = autoName,
                        MusicOptions = AvailableMusicLumps,
                        LevelName = prior.LevelName ?? "",
                        MusicName = prior.MusicName ?? "",
                        Author = prior.Author ?? "",
                    };
                    entry.PropertyChanged += OnMapPropertyChanged;
                    Maps.Add(entry);
                }
            }
            catch (WadException ex)
            {
                AppendLog($"[ERROR] {ex.Message}");
            }
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

            if (renames.Count > 0)
                AppendLog($"[INFO] Música renombrada por nombre duplicado: {string.Join("; ", renames)}");
        }
        finally
        {
            foreach (var w in opened)
                w.Dispose();
        }
    }

    private void OnMapPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(MapEntryViewModel.FinalName) || sender is not MapEntryViewModel map)
            return;

        var key = (map.WadPath, map.OriginalName);
        bool matchesAuto = map.AutoAssignedName is not null
            && string.Equals(map.FinalName.Trim(), map.AutoAssignedName, StringComparison.OrdinalIgnoreCase);

        if (matchesAuto)
            _userEditedSlots.Remove(key);
        else
            _userEditedSlots.Add(key);
    }

    // ------------------------------------------------------------------
    // Project (de)serialization
    // ------------------------------------------------------------------

    public void SaveProject(string path)
    {
        var data = new ProjectFileData
        {
            BaseWadPath = BaseWadPath,
            OutputPath = OutputPath,
            WadPaths = InputWads.Select(w => w.Path).ToList(),
            ResourceWadPaths = ResourceWads.Select(w => w.Path).ToList(),
            AutoAssignMaps = AutoAssignMaps,
            FilterResourcesToUsed = FilterResourcesToUsed,
            Maps = Maps
                .Select(m => new MapEntryData
                {
                    WadPath = m.WadPath,
                    OriginalName = m.OriginalName,
                    FinalName = m.FinalName,
                    IsUdmf = m.IsUdmf,
                    LevelName = m.LevelName,
                    MusicName = m.MusicName,
                    Author = m.Author,
                })
                .ToList(),
        };

        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
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
        BaseWadPath = data.BaseWadPath;
        OutputPath = data.OutputPath;
        AutoAssignMaps = data.AutoAssignMaps;
        FilterResourcesToUsed = data.FilterResourcesToUsed;

        foreach (string wadPath in data.WadPaths)
            AddWadsPathOnly(wadPath);

        foreach (string wadPath in data.ResourceWadPaths)
            AddResourceWadsPathOnly(wadPath);

        RefreshMusicOptions();

        // Restore map slots.
        var loaded = data.Maps
            .Select(m => new MapEntryViewModel(m.WadPath, m.OriginalName, m.FinalName, m.IsUdmf)
            {
                LevelName = m.LevelName ?? "",
                MusicName = m.MusicName ?? "",
                Author = m.Author ?? "",
                MusicOptions = AvailableMusicLumps,
            })
            .ToList();
        Maps.Clear();
        foreach (var m in loaded)
        {
            Maps.Add(m);
            _userEditedSlots.Add((m.WadPath, m.OriginalName));
        }

        AppendLog($"[INFO] Proyecto cargado desde {path}");
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

        var assignments = new List<MapAssignment>(Maps.Count);
        foreach (var map in Maps)
        {
            string final = map.FinalName.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(final))
            {
                AppendLog($"[ERROR] El mapa '{map.OriginalName}' de '{map.WadPath}' no tiene slot final.");
                return;
            }
            if (!string.Equals(final, map.FinalName, StringComparison.OrdinalIgnoreCase))
                map.FinalName = final;

            string? music = string.IsNullOrWhiteSpace(map.MusicName) ? null : map.MusicName.Trim();
            string? author = string.IsNullOrWhiteSpace(map.Author) ? null : map.Author.Trim();
            assignments.Add(new MapAssignment(map.WadPath, map.OriginalName, final, map.LevelName, music, author));
        }

        var request = new MergeRequest
        {
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