namespace CommunityWadCompiler.App.ViewModels;

/// <summary>One loaded WAD file shown in the inputs or resources list.</summary>
public sealed class WadEntryViewModel : ObservableObject
{
    private string _status = "Cargado";

    public required string Path { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string WadTypeLabel { get; init; } = "";

    public int MapCount { get; init; }

    /// <summary>Human-readable role of the WAD (aportado / recursos).</summary>
    public string Kind { get; init; } = "aportado";

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Display
    {
        get
        {
            string detail = MapCount > 0 ? $" | {MapCount} mapa(s)" : "";
            return $"{FileName}   [{WadTypeLabel} | {Kind}]{detail}";
        }
    }
}