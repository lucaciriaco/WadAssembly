using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CommunityWadCompiler.App.ViewModels;

namespace CommunityWadCompiler.App.Views;

public partial class ProjectSettingsWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    private readonly string _origProjectName = "";
    private readonly string _origVersionPrefix = "";
    private readonly string? _origBaseWadPath;
    private readonly int _origSlotCount;
    private readonly List<string> _origCollaborators = new();

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
        _viewModel.RebuildSlots();
        Close();
    }

    private void OnAddCollaborator(object? sender, RoutedEventArgs e)
        => _viewModel.Collaborators.Add(new CollaboratorEntryViewModel(""));

    private void OnRemoveCollaborator(object? sender, RoutedEventArgs e)
    {
        if (CollaboratorList.SelectedItem is CollaboratorEntryViewModel selected)
            _viewModel.Collaborators.Remove(selected);
    }

    private async void OnBrowseBaseWad(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar WAD base (IWAD)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("WAD files") { Patterns = new[] { "*.wad" } } },
        });
        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            _viewModel.BaseWadPath = path;
    }

    private void OnClearBaseWad(object? sender, RoutedEventArgs e) => _viewModel.BaseWadPath = null;

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
            Title = "Seleccionar WADs",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("WAD files") { Patterns = new[] { "*.wad" } } },
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToArray();
    }
}