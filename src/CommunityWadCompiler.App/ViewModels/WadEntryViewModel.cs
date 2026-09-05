namespace CommunityWadCompiler.App.ViewModels;

/// <summary>One loaded input WAD shown in the inputs list.</summary>
public sealed class WadEntryViewModel : ObservableObject
{
    private string _status = "Cargado";

    public required string Path { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string WadTypeLabel { get; init; } = "";

    public int MapCount { get; init; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Display =>
        $"{FileName}   [{WadTypeLabel}]   {MapCount} mapa(s)";
}