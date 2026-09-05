namespace CommunityWadCompiler.App.ViewModels;

/// <summary>One map with its editable final slot.</summary>
public sealed class MapEntryViewModel : ObservableObject
{
    private string _finalName;

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

    public string FinalName
    {
        get => _finalName;
        set => SetProperty(ref _finalName, value);
    }
}