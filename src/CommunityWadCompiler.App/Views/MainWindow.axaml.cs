using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityWadCompiler.App.ViewModels;

namespace CommunityWadCompiler.App.Views;

public partial class MainWindow : Window
{
    private const string SlotRowFormat = "SlotRowSource";
    private const string MapSourceFormat = "MapSource";

    private readonly MainWindowViewModel _viewModel = new();

    private SlotRowViewModel? _dragCandidate;
    private Point _dragPressPoint;
    private WadEntryViewModel? _wadDragCandidate;
    private Point _wadDragPressPoint;

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
    // Output
    // ------------------------------------------------------------------

    private async void OnBrowseOutput(object? sender, RoutedEventArgs e)
        => _viewModel.OutputPath = await PickOutputPathAsync() ?? _viewModel.OutputPath;

    // ------------------------------------------------------------------
    // Slot rows drag & drop (reorder the plan sheet)
    // ------------------------------------------------------------------

#pragma warning disable CS0618 // Avalonia's new async drag API adds complexity with no benefit here.

    private void OnSlotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint((Visual)sender!).Properties.IsLeftButtonPressed)
            return;
        // Do not start a drag from text/selection inputs or the music picker button.
        if (e.Source is TextBox or ComboBox or Button)
            return;

        if (sender is Control { DataContext: SlotRowViewModel row })
        {
            _dragPressPoint = e.GetPosition((Visual)sender!);
            _dragCandidate = row;
        }
    }

    private async void OnSlotPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is null)
            return;
        if (!e.GetCurrentPoint((Visual)sender!).Properties.IsLeftButtonPressed)
        {
            _dragCandidate = null;
            return;
        }

        var delta = e.GetPosition((Visual)sender!) - _dragPressPoint;
        if (delta.X * delta.X + delta.Y * delta.Y < 16)
            return;

        var data = new DataObject();
        data.Set(SlotRowFormat, _dragCandidate);
        _dragCandidate = null;
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void OnSlotPointerReleased(object? sender, PointerReleasedEventArgs e)
        => _dragCandidate = null;

    private void OnClearSlotFields(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not SlotRowViewModel row)
            return;
        int index = _viewModel.SlotRows.IndexOf(row);
        if (index < 0)
            return;
        _viewModel.ClearSlot(index);
    }

    /// <summary>Opens the music picker for the slot whose row button was clicked.</summary>
    private async void OnPickMusic(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SlotRowViewModel row)
            return;
        if (row.MusicOptions is not { Count: > 0 } options)
            return;

        var picker = new MusicPickerWindow(options, row.MusicName ?? "");
        await picker.ShowDialog(this);
        if (picker.Accepted)
        {
            row.MusicExternalPath = picker.SelectedExternalPath;
            row.MusicName = picker.SelectedName;
        }
    }

    /// <summary>Opens the sky picker for the slot whose row button was clicked.</summary>
    private async void OnPickSky(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SlotRowViewModel row)
            return;

        var textures = _viewModel.GetSkyTextureNames();
        var picker = new SkyPickerWindow(textures, row.SkyName ?? "sky1");
        picker.SetViewModel(_viewModel);
        await picker.ShowDialog(this);
        if (picker.Accepted)
            row.SkyName = picker.SelectedSkyName;
    }

    private void OnSlotDragOver(object? sender, DragEventArgs e)
    {
        bool valid = e.Data.Contains(SlotRowFormat) || e.Data.Contains(MapSourceFormat);
        e.DragEffects = valid ? DragDropEffects.Move : DragDropEffects.None;

        if (!valid || sender is not ItemsControl items)
        {
            _viewModel.SetDropTargetIndex(null);
            return;
        }

        int dropIndex = ComputeDropIndex(items, e.GetPosition(items));
        if (e.Data.Contains(MapSourceFormat))
        {
            // A map lands on the row under the pointer.
            _viewModel.SetDropTargetIndex(Math.Clamp(dropIndex, 0, _viewModel.SlotRows.Count - 1));
        }
        else
        {
            // Reordering a row: highlight the insertion point row.
            // If dropIndex == Count (after last row), highlight the last row.
            int highlightIndex = Math.Min(dropIndex, Math.Max(0, _viewModel.SlotRows.Count - 1));
            _viewModel.SetDropTargetIndex(highlightIndex);
        }
    }

    private void OnSlotDragLeave(object? sender, DragEventArgs e)
        => _viewModel.SetDropTargetIndex(null);

    private void OnSlotDrop(object? sender, DragEventArgs e)
    {
        _viewModel.SetDropTargetIndex(null);
        if (sender is not ItemsControl items)
            return;
        var point = e.GetPosition(items);

        // A map dragged from an input WAD fills (or replaces) the slot under the pointer.
        if (e.Data.Get(MapSourceFormat) is (string wadPath, string mapName))
        {
            var wadEntry = _viewModel.InputWads
                .FirstOrDefault(w => string.Equals(w.Path, wadPath, StringComparison.OrdinalIgnoreCase));
            var map = wadEntry?.Maps.FirstOrDefault(m => m.OriginalName == mapName);
            if (wadEntry is null || map is null)
                return;

            int slot = Math.Clamp(ComputeDropIndex(items, point), 0, _viewModel.SlotRows.Count - 1);
            _viewModel.AssignMapToSlot(slot, wadEntry, map);
            e.DragEffects = DragDropEffects.Move;
            return;
        }

        // A slot row dragged within the sheet reorders the plan.
        if (e.Data.Get(SlotRowFormat) is not SlotRowViewModel dragged)
            return;

        int current = _viewModel.SlotRows.IndexOf(dragged);
        if (current < 0)
            return;

        int target = ComputeDropIndex(items, point);
        if (current != target)
        {
            _viewModel.SlotRows.Move(current, target);
            _viewModel.RenumberSlotsByPosition();
        }
    }

    /// <summary>Insertion index for a drop: the first row whose center is below the pointer.</summary>
    private int ComputeDropIndex(ItemsControl items, Point point)
    {
        int target = _viewModel.SlotRows.Count;
        for (int i = 0; i < _viewModel.SlotRows.Count; i++)
        {
            var container = items.ContainerFromIndex(i);
            if (container is null)
                continue;
            var rel = container.TranslatePoint(new Point(0, 0), items);
            if (rel is { } r && point.Y < r.Y + container.Bounds.Height / 2)
            {
                target = i;
                break;
            }
        }
        return target;
    }

#pragma warning restore CS0618

    // ------------------------------------------------------------------
    // Input WAD map drag (drag a map from the WADs list into a slot)
    // ------------------------------------------------------------------

#pragma warning disable CS0618 // see above

    private void OnWadPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint((Visual)sender!).Properties.IsLeftButtonPressed)
            return;
        // Do not start a drag from the map picker dropdown.
        if (e.Source is ComboBox)
            return;

        if (sender is Control { DataContext: WadEntryViewModel wad } && wad.Maps.Count > 0)
        {
            _wadDragPressPoint = e.GetPosition((Visual)sender!);
            _wadDragCandidate = wad;
        }
    }

    private async void OnWadPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_wadDragCandidate is null)
            return;
        if (!e.GetCurrentPoint((Visual)sender!).Properties.IsLeftButtonPressed)
        {
            _wadDragCandidate = null;
            return;
        }

        var delta = e.GetPosition((Visual)sender!) - _wadDragPressPoint;
        if (delta.X * delta.X + delta.Y * delta.Y < 16)
            return;

        string mapName = _wadDragCandidate.Maps.Count == 1
            ? _wadDragCandidate.Maps[0].OriginalName
            : _wadDragCandidate.SelectedMapName ?? _wadDragCandidate.Maps[0].OriginalName;

        var data = new DataObject();
        data.Set(MapSourceFormat, (_wadDragCandidate.Path, mapName));
        _wadDragCandidate = null;
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void OnWadPointerReleased(object? sender, PointerReleasedEventArgs e)
        => _wadDragCandidate = null;

#pragma warning restore CS0618

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

    private async void OnSaveProject(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentProjectPath is not null)
        {
            _viewModel.SaveProject(_viewModel.CurrentProjectPath);
        }
        else if (await PickProjectFileAsync(open: false) is { } path)
        {
            _viewModel.SaveProject(path);
        }
    }

    private async void OnSaveProjectAs(object? sender, RoutedEventArgs e)
    {
        if (await PickProjectFileAsync(open: false) is { } path)
            _viewModel.SaveProject(path);
    }

    private void OnProjectSettings(object? sender, RoutedEventArgs e)
    {
        var settings = new ProjectSettingsWindow(_viewModel);
        settings.ShowDialog(this);
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