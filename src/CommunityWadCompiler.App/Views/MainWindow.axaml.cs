using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityWadCompiler.App.ViewModels;

namespace CommunityWadCompiler.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ------------------------------------------------------------------
    // File dialogs
    // ------------------------------------------------------------------

    private IStorageProvider Storage => StorageProvider;

    private async Task<string[]?> PickWadsAsync()
    {
        var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar WADs aportados",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("WAD files") { Patterns = new[] { "*.wad" } } },
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToArray();
    }

    private async Task<string?> PickBaseWadAsync()
    {
        var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar WAD base (IWAD)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("WAD files") { Patterns = new[] { "*.wad" } } },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickOutputPathAsync()
    {
        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar WAD compilado",
            SuggestedFileName = "megawad.wad",
            DefaultExtension = "wad",
            FileTypeChoices = new[] { new FilePickerFileType("WAD files") { Patterns = new[] { "*.wad" } } },
        });
        return file?.TryGetLocalPath();
    }

    private async Task<string?> PickProjectFileAsync(bool open)
    {
        var filter = new[] { new FilePickerFileType("Proyecto Community Wad") { Patterns = new[] { "*.json" } } };
        if (open)
        {
            var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Abrir proyecto",
                AllowMultiple = false,
                FileTypeFilter = filter,
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        }

        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar proyecto",
            SuggestedFileName = "proyecto.json",
            DefaultExtension = "json",
            FileTypeChoices = filter,
        });
        return file?.TryGetLocalPath();
    }

    // ------------------------------------------------------------------
    // Input WAD buttons
    // ------------------------------------------------------------------

    private async void OnAddWads(object? sender, RoutedEventArgs e)
    {
        if (await PickWadsAsync() is { Length: > 0 } paths)
            _viewModel.AddWads(paths);
    }

    private void OnRemoveWad(object? sender, RoutedEventArgs e) => _viewModel.RemoveSelectedWad();

    private void OnMoveUp(object? sender, RoutedEventArgs e) => _viewModel.MoveSelectedWad(-1);

    private void OnMoveDown(object? sender, RoutedEventArgs e) => _viewModel.MoveSelectedWad(+1);

    // ------------------------------------------------------------------
    // Base / output
    // ------------------------------------------------------------------

    private async void OnBrowseBaseWad(object? sender, RoutedEventArgs e)
        => _viewModel.BaseWadPath = await PickBaseWadAsync() ?? _viewModel.BaseWadPath;

    private void OnClearBaseWad(object? sender, RoutedEventArgs e) => _viewModel.BaseWadPath = null;

    private async void OnBrowseOutput(object? sender, RoutedEventArgs e)
        => _viewModel.OutputPath = await PickOutputPathAsync() ?? _viewModel.OutputPath;

    // ------------------------------------------------------------------
    // Compile
    // ------------------------------------------------------------------

    private async void OnCompile(object? sender, RoutedEventArgs e) => await _viewModel.CompileAsync();

    // ------------------------------------------------------------------
    // Menu
    // ------------------------------------------------------------------

    private void OnNewProject(object? sender, RoutedEventArgs e) => _viewModel.Clear();

    private async void OnOpenProject(object? sender, RoutedEventArgs e)
    {
        if (await PickProjectFileAsync(open: true) is { } path)
            _viewModel.LoadProject(path);
    }

    private async void OnSaveProjectAs(object? sender, RoutedEventArgs e)
    {
        if (await PickProjectFileAsync(open: false) is { } path)
            _viewModel.SaveProject(path);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnAbout(object? sender, RoutedEventArgs e)
    {
        var about = new Window
        {
            Title = "Acerca de",
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap,
                Text = "Community Wad Compiler\n\n" +
                       "Compila WADs de varios autores en un único PWAD.\n" +
                       "Soporta mapas Doom clásico y UDMF, fusión de TEXTURE1/PNAMES\n" +
                       "y deduplicación de recursos.\n\n" +
                       "Estructura: librería CommunityWadCompiler.Core + UI Avalonia.",
            },
        };
        about.ShowDialog(this);
    }
}