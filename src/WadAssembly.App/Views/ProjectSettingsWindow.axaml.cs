using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using WadAssembly.App.Services;
using WadAssembly.App.ViewModels;

namespace WadAssembly.App.Views;

public partial class ProjectSettingsWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    private readonly string _origProjectName = "";
    private readonly string _origVersionPrefix = "";
    private readonly string? _origBaseWadPath;
    private readonly int _origSlotCount;
    private readonly List<string> _origCollaborators = new();
    private readonly string _origIntermissionMusic = "";
    private readonly string _origIntermissionMusicExternalPath = "";
    private readonly string _origSourcePortPath = "";

    public ProjectSettingsWindow()
    {
        InitializeComponent();
    }

    public ProjectSettingsWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.SyncCollaboratorsFromAuthors();
        _origProjectName = viewModel.ProjectName;
        _origVersionPrefix = viewModel.VersionPrefix;
        _origBaseWadPath = viewModel.BaseWadPath;
        _origSlotCount = viewModel.SlotCount;
        _origCollaborators = viewModel.Collaborators
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
        _origIntermissionMusic = viewModel.IntermissionMusic;
        _origIntermissionMusicExternalPath = viewModel.IntermissionMusicExternalPath;

        // Refresh the source port options from the app config (in case new ports were
        // added meanwhile) and remember the current selection for Cancel.
        _viewModel.RefreshSourcePortOptions(viewModel.SelectedSourcePort?.ExecutablePath);
        _origSourcePortPath = viewModel.SelectedSourcePort?.ExecutablePath ?? "";
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        _viewModel.RebuildSlots();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _viewModel.ProjectName = _origProjectName;
        _viewModel.VersionPrefix = _origVersionPrefix;
        _viewModel.BaseWadPath = _origBaseWadPath;
        _viewModel.SlotCount = _origSlotCount;
        _viewModel.Collaborators.Clear();
        foreach (string name in _origCollaborators)
            _viewModel.Collaborators.Add(new CollaboratorEntryViewModel(name));
        _viewModel.IntermissionMusic = _origIntermissionMusic;
        _viewModel.IntermissionMusicExternalPath = _origIntermissionMusicExternalPath;
        _viewModel.RefreshSourcePortOptions(_origSourcePortPath);
        _viewModel.RebuildSlots();
        Close();
    }

    private void OnAddCollaborator(object? sender, RoutedEventArgs e)
        => _viewModel.Collaborators.Add(new CollaboratorEntryViewModel(""));

    private void OnRemoveCollaborator(object? sender, RoutedEventArgs e)
    {
        var list = this.FindControl<ListBox>("CollaboratorList");
        if (list?.SelectedItem is CollaboratorEntryViewModel selected)
        {
            string removedAuthor = selected.Name;
            _viewModel.Collaborators.Remove(selected);
            
            // Also remove maps authored by this collaborator from the slot rows.
            _viewModel.RemoveMapsByAuthor(removedAuthor);
        }
    }

    private async void OnBrowseBaseWad(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LanguageService.GetString("Dialog.PickBaseWad"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("WAD files") { Patterns = new[] { "*.wad" } } },
        });
        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            _viewModel.BaseWadPath = path;
    }

    private void OnClearBaseWad(object? sender, RoutedEventArgs e) => _viewModel.BaseWadPath = null;

    /// <summary>Opens the music picker for the intermission theme. The "no music"
    /// sentinel is always available even when no WAD is loaded.</summary>
    private async void OnPickIntermissionMusic(object? sender, RoutedEventArgs e)
    {
        var options = _viewModel.AvailableMusicLumps.ToList();
        if (options.All(o => !string.IsNullOrWhiteSpace(o.WadPath)))
            options.Add(new MusicLumpInfo(SlotRowViewModel.NoMusicOption, ""));

        var picker = new MusicPickerWindow(options, _viewModel.IntermissionMusic ?? "");
        await picker.ShowDialog(this);
        if (picker.Accepted)
        {
            _viewModel.IntermissionMusic = picker.SelectedName;
            _viewModel.IntermissionMusicExternalPath = picker.SelectedExternalPath;
        }
    }

    private void OnClearIntermissionMusic(object? sender, RoutedEventArgs e)
    {
        _viewModel.IntermissionMusic = "";
        _viewModel.IntermissionMusicExternalPath = "";
    }

    private async void OnAddResources(object? sender, RoutedEventArgs e)
    {
        if (await PickWadsAsync() is { Length: > 0 } paths)
            _viewModel.AddResourceWads(paths);
    }

    private void OnRemoveResource(object? sender, RoutedEventArgs e) => _viewModel.RemoveSelectedResourceWad();

    private void OnMoveResourceUp(object? sender, RoutedEventArgs e) => _viewModel.MoveSelectedResourceWad(-1);

    private void OnMoveResourceDown(object? sender, RoutedEventArgs e) => _viewModel.MoveSelectedResourceWad(+1);

    private async Task<string[]?> PickWadsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LanguageService.GetString("Dialog.PickWads"),
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("WAD files") { Patterns = new[] { "*.wad" } } },
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToArray();
    }
}