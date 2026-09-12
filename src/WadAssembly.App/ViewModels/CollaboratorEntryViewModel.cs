namespace WadAssembly.App.ViewModels;

/// <summary>One project collaborator (map author) editable in the project settings popup.</summary>
public sealed class CollaboratorEntryViewModel : ObservableObject
{
    private string _name;

    public CollaboratorEntryViewModel(string name) => _name = name;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
}
