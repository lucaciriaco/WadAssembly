using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityWadCompiler.App.Models;
using CommunityWadCompiler.App.Services;
using CommunityWadCompiler.App.ViewModels;

namespace CommunityWadCompiler.App.Views;

public partial class MainWindow : Window
{
    private const string SlotRowFormat = "SlotRowSource";
    private const string MapSourceFormat = "MapSource";
    private const string ColumnHeaderFormat = "ColumnHeaderSource";

    private readonly MainWindowViewModel _viewModel = new();

    private Grid _headerGrid = null!;
    private ItemsControl _rowsControl = null!;

    private PlanColumnWidths? _planColumnWidths;

    private SlotRowViewModel? _dragCandidate;
    private Point _dragPressPoint;
    private WadEntryViewModel? _wadDragCandidate;
    private Point _wadDragPressPoint;

    /// <summary>Column ids (0..8) currently shown at each physical position.</summary>
    private int[] _columnOrder = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
    private int _headerDragId = -1;
    private Point _headerDragPressPoint;

    private readonly DispatcherTimer _configSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public MainWindow()
    {
        InitializeComponent();
        _headerGrid = this.FindControl<Grid>("HeaderGrid")!;
        _rowsControl = this.FindControl<ItemsControl>("RowsControl")!;
        _planColumnWidths = Resources["PlanColWidths"] as PlanColumnWidths;
        DataContext = _viewModel;

        // Re-position the cells of rows created after startup (project load, new slots)
        // so they match the saved / rearranged column order (see OnRowAttachedToVisualTree).

        var settings = AppSettingsService.Load();
        if (settings.ColumnWidths is { Length: 9 } widths && _planColumnWidths is not null)
        {
            for (int i = 0; i < 9; i++)
            {
                if (widths[i] > 0)
                    _planColumnWidths.SetWidth(i, new GridLength(widths[i]));
            }
        }
        if (IsValidColumnOrder(settings.ColumnOrder))
        {
            _columnOrder = settings.ColumnOrder;
            ApplyColumnPositions();
        }

        _configSaveTimer.Tick += (_, _) => { _configSaveTimer.Stop(); SaveConfigNow(); };
        Closing += (_, _) => SaveConfigNow();

        foreach (var header in _headerGrid.Children.OfType<TextBlock>())
        {
            header.PointerPressed += OnHeaderPointerPressed;
            header.PointerMoved += OnHeaderPointerMoved;
            header.PointerReleased += OnHeaderPointerReleased;
        }
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
            Title = LanguageService.GetString("Dialog.PickInputWads"),
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("WAD files") { Patterns = new[] { "*.wad" } } },
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToArray();
    }

    private async Task<string?> PickOutputPathAsync()
    {
        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LanguageService.GetString("Dialog.SaveOutputWad"),
            SuggestedFileName = "megawad.wad",
            DefaultExtension = "wad",
            FileTypeChoices = new[] { new FilePickerFileType("WAD files") { Patterns = new[] { "*.wad" } } },
        });
        return file?.TryGetLocalPath();
    }

    private async Task<string?> PickProjectFileAsync(bool open)
    {
        var filter = new[] { new FilePickerFileType(LanguageService.GetString("Dialog.ProjectFilter")) { Patterns = new[] { "*.json" } } };
        if (open)
        {
            var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LanguageService.GetString("Dialog.OpenProject"),
                AllowMultiple = false,
                FileTypeFilter = filter,
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        }

        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LanguageService.GetString("Dialog.SaveProject"),
            SuggestedFileName = LanguageService.GetString("Dialog.SuggestedProjectName"),
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

    // ------------------------------------------------------------------
    // Header column resizing (GridSplitter)
    // ------------------------------------------------------------------

    /// <summary>GridSplitter in the header moved: mirror the header widths into the
    /// shared <see cref="PlanColumnWidths"/> so every slot row stays aligned.</summary>
    private void OnColumnResized(object? sender, VectorEventArgs e) => SyncColumnWidths();

    private void SyncColumnWidths()
    {
        if (_planColumnWidths is null)
            return;

        var definitions = _headerGrid.ColumnDefinitions;
        _planColumnWidths.C0 = definitions[0].Width;
        _planColumnWidths.C1 = definitions[1].Width;
        _planColumnWidths.C2 = definitions[2].Width;
        _planColumnWidths.C3 = definitions[3].Width;
        _planColumnWidths.C4 = definitions[4].Width;
        _planColumnWidths.C5 = definitions[5].Width;
        _planColumnWidths.C6 = definitions[6].Width;
        _planColumnWidths.C7 = definitions[7].Width;
        _planColumnWidths.C8 = definitions[8].Width;
        ScheduleConfigSave();
    }

    // ------------------------------------------------------------------
    // Settings persistence (column widths + order, saved to the config JSON)
    // ------------------------------------------------------------------

    private static bool IsValidColumnOrder(int[]? order)
        => order is { Length: 9 } && order.Distinct().Count() == 9 && order.All(i => i is >= 0 and <= 8);

    /// <summary>Debounces the save while a splitter drag streams resize events.</summary>
    private void ScheduleConfigSave()
    {
        _configSaveTimer.Stop();
        _configSaveTimer.Start();
    }

    private void SaveConfigNow()
    {
        if (_planColumnWidths is null)
            return;
        var settings = new AppSettings
        {
            Language = LanguageService.CurrentLanguage,
            ColumnWidths = Enumerable.Range(0, 9).Select(i => _planColumnWidths.GetWidth(i).Value).ToArray(),
            ColumnOrder = _columnOrder.ToArray(),
        };
        AppSettingsService.Save(settings);
    }

    // ------------------------------------------------------------------
    // Header column reorder (drag a header cell to a new position)
    // ------------------------------------------------------------------

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint((Visual)sender!).Properties.IsLeftButtonPressed)
            return;

        if (sender is TextBlock { Tag: string tag } && int.TryParse(tag, out int id))
        {
            _headerDragPressPoint = e.GetPosition((Visual)sender!);
            _headerDragId = id;
        }
    }

    private async void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_headerDragId < 0)
            return;
        if (!e.GetCurrentPoint((Visual)sender!).Properties.IsLeftButtonPressed)
        {
            _headerDragId = -1;
            return;
        }

        var delta = e.GetPosition((Visual)sender!) - _headerDragPressPoint;
        if (delta.X * delta.X + delta.Y * delta.Y < 16)
            return;

        var data = new DataObject();
        data.Set(ColumnHeaderFormat, _headerDragId);
        _headerDragId = -1;
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
        => _headerDragId = -1;

    private void OnHeaderDragOver(object? sender, DragEventArgs e)
        => e.DragEffects = e.Data.Contains(ColumnHeaderFormat) ? DragDropEffects.Move : DragDropEffects.None;

    private void OnHeaderDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get(ColumnHeaderFormat) is not int dragId)
            return;

        var point = e.GetPosition(_headerGrid);
        ReorderColumns(dragId, ComputeHeaderDropIndex(point.X));
        e.DragEffects = DragDropEffects.Move;
    }

    /// <summary>Insertion position: the column whose center is to the right of the pointer.</summary>
    private int ComputeHeaderDropIndex(double x)
    {
        double running = 0;
        for (int i = 0; i < _columnOrder.Length; i++)
        {
            double width = _headerGrid.ColumnDefinitions[i].ActualWidth;
            if (x < running + width / 2)
                return i;
            running += width;
        }
        return _columnOrder.Length - 1;
    }

    /// <summary>Moves the column <paramref name="dragId"/> to physical position
    /// <paramref name="target"/>. The width stored at each old position travels with
    /// its column, then header cells and every slot row cell are re-positioned.</summary>
    private void ReorderColumns(int dragId, int target)
    {
        if (_planColumnWidths is null)
            return;
        int from = Array.IndexOf(_columnOrder, dragId);
        if (from < 0 || target < 0 || target >= _columnOrder.Length || from == target)
            return;

        var reordered = new List<int>(_columnOrder);
        reordered.RemoveAt(from);
        reordered.Insert(target, dragId);
        var newOrder = reordered.ToArray();

        var oldWidths = new GridLength[9];
        for (int i = 0; i < 9; i++)
            oldWidths[i] = _planColumnWidths.GetWidth(i);

        for (int i = 0; i < 9; i++)
            _planColumnWidths.SetWidth(i, oldWidths[Array.IndexOf(_columnOrder, newOrder[i])]);

        _columnOrder = newOrder;
        ApplyColumnPositions();
        SaveConfigNow();
    }

    /// <summary>Re-positions the header cells and every slot row cell so each column
    /// renders at its new physical position.</summary>
    private void ApplyColumnPositions()
    {
        foreach (var child in _headerGrid.Children)
        {
            if (child is TextBlock { Tag: string tag } && int.TryParse(tag, out int id))
                Grid.SetColumn(child, Array.IndexOf(_columnOrder, id));
        }

        for (int r = 0; r < _viewModel.SlotRows.Count; r++)
            LayoutRowCells(_rowsControl.ContainerFromIndex(r));
    }

    /// <summary>Called when a slot row is attached to the visual tree (initial and
    /// later-realized rows); places its cells at the current column order.</summary>
    private void OnRowAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => LayoutRowCells(sender as Control);

    private void LayoutRowCells(Control? container)
    {
        Grid? rowGrid = container switch
        {
            Grid grid => grid,
            ContentPresenter { Child: Grid child } => child,
            _ => null,
        };
        if (rowGrid is null)
            return;
        foreach (var child in rowGrid.Children)
        {
            if (child is Control { Tag: string tag } && int.TryParse(tag, out int id))
                Grid.SetColumn(child, Array.IndexOf(_columnOrder, id));
        }
    }

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

    private void OnLanguageSpanish(object? sender, RoutedEventArgs e) => LanguageService.SetLanguage(LanguageService.Spanish);

    private void OnLanguageEnglish(object? sender, RoutedEventArgs e) => LanguageService.SetLanguage(LanguageService.English);

    private void OnAbout(object? sender, RoutedEventArgs e)
    {
        var about = new Window
        {
            Title = LanguageService.GetString("Dialog.About"),
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new TextBlock
            {
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap,
                Text = LanguageService.GetString("Dialog.AboutText"),
            },
        };
        about.ShowDialog(this);
    }
}