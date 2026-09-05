namespace CommunityWadCompiler.App.ViewModels;

/// <summary>One map with its editable final slot, level name, author and music.</summary>
public sealed class MapEntryViewModel : ObservableObject
{
    private string _finalName;
    private string _levelName = "";
    private string _musicName = "";
    private string _author = "";

    public MapEntryViewModel(
        string wadPath,
        string originalName,
        string finalName,
        bool isUdmf)
    {
        WadPath = wadPath;
        OriginalName = originalName;
        _finalName = finalName;
        IsUdmf = isUdmf;
    }

    public string WadPath { get; }

    public string WadFileName => System.IO.Path.GetFileNameWithoutExtension(WadPath);

    public string OriginalName { get; }

    public bool IsUdmf { get; }

    /// <summary>Slot name generated automatically; null when the slot was never auto-assigned.</summary>
    public string? AutoAssignedName { get; set; }

    public string FormatLabel => IsUdmf ? "UDMF" : "Clásico";

    /// <summary>Music lumps available to pick from (shared list maintained by the view model).</summary>
    public IReadOnlyList<string>? MusicOptions { get; set; }

    public string FinalName
    {
        get => _finalName;
        set => SetProperty(ref _finalName, value);
    }

    /// <summary>Readable level name shown on the automap; used by the generated MAPINFO.</summary>
    public string LevelName
    {
        get => _levelName;
        set => SetProperty(ref _levelName, value);
    }

    /// <summary>Music lump assigned to this map; used by the generated MAPINFO (`music =`).</summary>
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
}