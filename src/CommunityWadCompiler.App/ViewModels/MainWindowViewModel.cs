using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CommunityWadCompiler.App.Models;
using CommunityWadCompiler.Core.Maps;
using CommunityWadCompiler.Core.Merge;
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
    private WadEntryViewModel? _selectedWad;

    public ObservableCollection<WadEntryViewModel> InputWads { get; } = new();

    public ObservableCollection<MapEntryViewModel> Maps { get; } = new();

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

    public IReadOnlyList<string> InputWadPaths => InputWads.Select(w => w.Path).ToList();

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

    public void Clear()
    {
        InputWads.Clear();
        Maps.Clear();
        _userEditedSlots.Clear();
        BaseWadPath = null;
        OutputPath = null;
        AutoAssignMaps = true;
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
            m => m.FinalName);

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

                    if (_userEditedSlots.Contains(key) && existing.TryGetValue(key, out string? kept))
                    {
                        finalName = kept;
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

                    var entry = new MapEntryViewModel(wadEntry.Path, map.OriginalName, finalName, map.IsUdmf)
                    {
                        AutoAssignedName = autoName,
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
            AutoAssignMaps = AutoAssignMaps,
            Maps = Maps
                .Select(m => new MapEntryData
                {
                    WadPath = m.WadPath,
                    OriginalName = m.OriginalName,
                    FinalName = m.FinalName,
                    IsUdmf = m.IsUdmf,
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

        foreach (string wadPath in data.WadPaths)
            AddWadsPathOnly(wadPath);

        // Restore map slots.
        var loaded = data.Maps
            .Select(m => new MapEntryViewModel(m.WadPath, m.OriginalName, m.FinalName, m.IsUdmf))
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
            assignments.Add(new MapAssignment(map.WadPath, map.OriginalName, final));
        }

        var request = new MergeRequest
        {
            BaseWadPath = BaseWadPath,
            InputWadPaths = InputWadPaths,
            OutputPath = OutputPath,
            MapAssignments = assignments,
            Options = new MergeOptions { AutoAssignMaps = false },
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