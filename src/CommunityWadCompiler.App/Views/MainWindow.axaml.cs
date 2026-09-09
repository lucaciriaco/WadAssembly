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
    private Grid _layoutGrid = null!;
    private Panel _leftPanel = null!;
    private Button _hamburgerButton = null!;
    private GridSplitter _leftPanelSplitter = null!;
    private Avalonia.Controls.Shapes.Path _collapseIcon = null!;
    private bool _leftPanelCollapsed;
    private GridLength _leftPanelExpandedWidth = new(1, GridUnitType.Star);

    private Grid _logPanel = null!;
    private GridSplitter _logSplitter = null!;
    private bool _logVisible = true;
    private GridLength _savedLogRowHeight = new(1, GridUnitType.Star);

    private PlanColumnWidths? _planColumnWidths;
    private readonly PlanColumnsViewModel _planColumns = new();

    private SlotRowViewModel? _dragCandidate;
    private Point _dragPressPoint;
    private WadEntryViewModel? _wadDragCandidate;
    private Point _wadDragPressPoint;

    private int _headerDragId = -1;
    private Point _headerDragPressPoint;

    private MenuItem? _viewColumnsMenu;
    private List<object>? _viewColumnMenuItems;
    private MenuItem? _viewLogMenuItem;

    private TextBlock? _themeCheckSystem;
    private TextBlock? _themeCheckLight;
    private TextBlock? _themeCheckDark;

    private readonly DispatcherTimer _configSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public MainWindow()
    {
        InitializeComponent();
        _headerGrid = this.FindControl<Grid>("HeaderGrid")!;
        _rowsControl = this.FindControl<ItemsControl>("RowsControl")!;
        _layoutGrid = this.FindControl<Grid>("LayoutGrid")!;
        _leftPanel = this.FindControl<Panel>("LeftPanel")!;
        _hamburgerButton = this.FindControl<Button>("HamburgerButton")!;
        _leftPanelSplitter = this.FindControl<GridSplitter>("LeftPanelSplitter")!;
        _logPanel = this.FindControl<Grid>("LogPanel")!;
        _logSplitter = this.FindControl<GridSplitter>("LogSplitter")!;
        _collapseIcon = this.FindControl<Avalonia.Controls.Shapes.Path>("CollapseIcon")!;
        _planColumnWidths = Resources["PlanColWidths"] as PlanColumnWidths;
        DataContext = _viewModel;

        var settings = AppSettingsService.Load();
        _logVisible = settings.LogVisible ?? true;
        ApplyLogVisibility();

        _planColumns.ColumnsChanged += ApplyColumnChanges;
        _planColumns.LoadFrom(settings);
        ApplyColumnChanges();

        _configSaveTimer.Tick += (_, _) => { _configSaveTimer.Stop(); SaveConfigNow(); };
        Closing += (_, _) => SaveConfigNow();

        foreach (var header in _headerGrid.Children.OfType<TextBlock>())
        {
            header.PointerPressed += OnHeaderPointerPressed;
            header.PointerMoved += OnHeaderPointerMoved;
            header.PointerReleased += OnHeaderPointerReleased;

            // Right-click a header cell to show/hide any column (hidden ones included).
            var contextMenu = new ContextMenu();
            header.ContextMenu = contextMenu;
            contextMenu.Opened += (_, _) => PopulateColumnMenu(contextMenu);
        }

        _viewColumnsMenu = this.FindControl<MenuItem>("ViewColumnsMenu");
        if (_viewColumnsMenu is not null)
        {
            PopulateViewMenu();
            LanguageService.LanguageChanged += (_, _) => PopulateViewMenu();
        }

        if (this.FindControl<TextBlock>("LogTitle") is { } logTitle)
        {
            var logContextMenu = new ContextMenu();
            logTitle.ContextMenu = logContextMenu;
            logContextMenu.Opened += (_, _) =>
            {
                logContextMenu.Items.Clear();
                var hide = new MenuItem { Header = LanguageService.GetString("Log.Hide") };
                hide.Click += (_, _) => SetLogVisible(false);
                logContextMenu.Items.Add(hide);
            };
        }

        _themeCheckSystem = this.FindControl<TextBlock>("ThemeCheckSystem");
        _themeCheckLight = this.FindControl<TextBlock>("ThemeCheckLight");
        _themeCheckDark = this.FindControl<TextBlock>("ThemeCheckDark");
        UpdateThemeChecks();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Toggles the left "contributed WADs" panel: collapses it into the narrow
    /// toggle strip, giving the full width to the maps/slots grid. The arrow points
    /// right while collapsed (clicking expands) and left while open (clicking collapses).</summary>
    private void OnToggleLeftPanel(object? sender, RoutedEventArgs e)
    {
        _leftPanelCollapsed = !_leftPanelCollapsed;
        _leftPanel.IsVisible = !_leftPanelCollapsed;
        if (_leftPanelCollapsed)
        {
            // Remember the current width so it can be restored when re-expanding.
            _leftPanelExpandedWidth = _layoutGrid.ColumnDefinitions[1].Width;
            _layoutGrid.ColumnDefinitions[1].Width = GridLength.Auto;
        }
        else
        {
            _layoutGrid.ColumnDefinitions[1].Width = _leftPanelExpandedWidth;
        }
        _leftPanelSplitter.IsVisible = !_leftPanelCollapsed;
        _collapseIcon.Data = _leftPanelCollapsed
            ? Resources["Icon.ArrowRight"] as StreamGeometry
            : Resources["Icon.ArrowLeft"] as StreamGeometry;
        ToolTip.SetTip(_hamburgerButton, LanguageService.GetString(
            _leftPanelCollapsed ? "Input.Expand" : "Input.Collapse"));
    }

    /// <summary>Remembers the left panel width the user set by dragging the vertical
    /// splitter, so the collapse/expand toggle restores exactly that width.</summary>
    private void OnLeftPanelSplitterCompleted(object? sender, VectorEventArgs e)
    {
        if (_leftPanelCollapsed)
            return;
        _leftPanelExpandedWidth = _layoutGrid.ColumnDefinitions[1].Width;
    }

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
    /// shared <see cref="PlanColumnWidths"/> so every slot row stays aligned, and keep
    /// each visible column's width in the persisted column state.</summary>
    private void OnColumnResized(object? sender, VectorEventArgs e) => SyncColumnWidths();

    private void SyncColumnWidths()
    {
        if (_planColumnWidths is null)
            return;

        var definitions = _headerGrid.ColumnDefinitions;
        for (int i = 0; i < _planColumns.Columns.Count; i++)
        {
            var column = _planColumns.ColumnAt(i);
            if (column.Visible)
            {
                column.Width = definitions[i].Width.Value;
                _planColumnWidths.SetWidth(i, definitions[i].Width);
            }
            else
            {
                // Hidden columns stay collapsed whatever the splitter tried to do.
                _planColumnWidths.SetWidth(i, new GridLength(0));
            }
        }
        ScheduleConfigSave();
    }

    // ------------------------------------------------------------------
    // Settings persistence (column widths + order + visibility, saved to the config JSON)
    // ------------------------------------------------------------------

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
        var settings = new AppSettings { Language = LanguageService.CurrentLanguage, LogVisible = _logVisible, Theme = ThemeService.CurrentTheme };
        _planColumns.SaveTo(settings);
        AppSettingsService.Save(settings);
    }

    // ------------------------------------------------------------------
    // Console (log) visibility
    // ------------------------------------------------------------------

    /// <summary>Shows or hides the bottom console panel. Hidden rows are collapsed to
    /// zero height (and their MinHeight lifted), so the plan sheet gets the full area;
    /// the menu item acts both ways (checkmark = visible).</summary>
    private void SetLogVisible(bool visible)
    {
        if (_logVisible == visible)
            return;
        _logVisible = visible;
        ApplyLogVisibility();
        SaveConfigNow();
    }

    /// <summary>Applies the current <see cref="_logVisible"/> state to the layout and
    /// to the Configuration → View menu item (called at startup and on toggle).</summary>
    private void ApplyLogVisibility()
    {
        _logPanel.IsVisible = _logVisible;
        _logSplitter.IsVisible = _logVisible;

        var logRow = _layoutGrid.RowDefinitions[2];
        if (_logVisible)
        {
            logRow.Height = _savedLogRowHeight;
            logRow.MinHeight = 80;
        }
        else
        {
            _savedLogRowHeight = logRow.Height;
            logRow.Height = new GridLength(0);
            logRow.MinHeight = 0;
        }

        if (_viewLogMenuItem is not null)
        {
            _viewLogMenuItem.Header = LanguageService.GetString(_logVisible ? "Log.Hide" : "Log.Show");
            if (_viewLogMenuItem.Icon is TextBlock icon)
                icon.Text = _logVisible ? "✓" : " ";
        }
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
        for (int i = 0; i < _planColumns.Columns.Count; i++)
        {
            double width = _headerGrid.ColumnDefinitions[i].ActualWidth;
            if (x < running + width / 2)
                return i;
            running += width;
        }
        return _planColumns.Columns.Count - 1;
    }

    /// <summary>Moves the column <paramref name="dragId"/> to physical position
    /// <paramref name="target"/>. Each column keeps its own width, so the width follows
    /// the column automatically; the change handler re-applies the physical grid and
    /// re-positions header cells and every slot row cell.</summary>
    private void ReorderColumns(int dragId, int target)
    {
        int from = _planColumns.IndexOfId(dragId);
        if (from < 0 || target < 0 || target >= _planColumns.Columns.Count || from == target)
            return;

        _planColumns.Move(from, target);
        SaveConfigNow();
    }

    /// <summary>Re-applies everything derived from the column state: physical widths,
    /// cell positions and hidden-column visuals.</summary>
    private void ApplyColumnChanges()
    {
        if (_planColumnWidths is null)
            return;
        _planColumns.ApplyTo(_planColumnWidths);
        ApplyColumnPositions();
        UpdateColumnVisuals();
        UpdateViewColumnChecks();
    }

    /// <summary>Hides / shows the column with <paramref name="id"/> and persists it.
    /// Used by the header context menu and the Configuration → View menu.</summary>
    private void ToggleColumnVisibility(int id)
    {
        var column = _planColumns.ColumnById(id);
        if (column is null)
            return;
        column.Visible = !column.Visible;
        SaveConfigNow();
    }

    /// <summary>Re-positions the header cells and every slot row cell so each column
    /// renders at its current physical position.</summary>
    private void ApplyColumnPositions()
    {
        foreach (var child in _headerGrid.Children)
        {
            if (child is TextBlock { Tag: string tag } && int.TryParse(tag, out int id))
            {
                int pos = _planColumns.IndexOfId(id);
                if (pos >= 0)
                    Grid.SetColumn(child, pos);
            }
        }

        for (int r = 0; r < _viewModel.SlotRows.Count; r++)
            LayoutRowCells(_rowsControl.ContainerFromIndex(r));
    }

    /// <summary>Collapses the cells of hidden columns (header + rows) and disables the
    /// splitters that sit next to a hidden column, so a hidden slot cannot be stretched.</summary>
    private void UpdateColumnVisuals()
    {
        foreach (var child in _headerGrid.Children)
        {
            switch (child)
            {
                case TextBlock { Tag: string tag } when int.TryParse(tag, out int id):
                    child.IsVisible = _planColumns.ColumnById(id)?.Visible ?? true;
                    break;

                case GridSplitter splitter:
                {
                    int pos = Grid.GetColumn(splitter);
                    if (pos >= 1 && pos < _planColumns.Columns.Count)
                    {
                        bool leftVisible = _planColumns.ColumnAt(pos - 1).Visible;
                        bool rightVisible = _planColumns.ColumnAt(pos).Visible;
                        splitter.IsEnabled = leftVisible && rightVisible;
                    }
                    break;
                }

                case Border border when border.Classes.Contains("divider"):
                {
                    int pos = Grid.GetColumn(border);
                    if (pos >= 1 && pos < _planColumns.Columns.Count)
                        border.IsVisible = _planColumns.ColumnAt(pos).Visible;
                    break;
                }
            }
        }
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
            {
                int pos = _planColumns.IndexOfId(id);
                if (pos >= 0)
                    Grid.SetColumn(child, pos);
                child.IsVisible = _planColumns.ColumnById(id)?.Visible ?? true;
            }
        }
    }

    /// <summary>Builds one toggle item per column (checkmark = visible) for the header
    /// context menu and the Configuration → View menu.</summary>
    private List<MenuItem> CreateColumnMenuItems()
    {
        var items = new List<MenuItem>();
        foreach (var column in _planColumns.Columns)
        {
            var item = new MenuItem
            {
                Header = column.Title,
                Icon = new TextBlock
                {
                    Text = column.Visible ? "✓" : " ",
                    MinWidth = 16,
                    TextAlignment = TextAlignment.Center,
                },
                Tag = column.Id,
            };
            int id = column.Id;
            item.Click += (_, _) => ToggleColumnVisibility(id);
            items.Add(item);
        }
        return items;
    }

    private void PopulateColumnMenu(ItemsControl menu)
    {
        menu.ItemsSource = CreateColumnMenuItems();
    }

    /// <summary>Populates the Configuration → View submenu: one toggle per column
    /// (checkmark = visible) plus the console show/hide toggle.</summary>
    private void PopulateViewMenu()
    {
        if (_viewColumnsMenu is null)
            return;
        var items = new List<object>(CreateColumnMenuItems());
        items.Add(new Separator());
        items.Add(CreateLogMenuItem());
        _viewColumnsMenu.ItemsSource = items;
        _viewColumnMenuItems = items;
    }

    /// <summary>Builds the "Show/Hide console" toggle for the View submenu. Its header and
    /// checkmark are refreshed by <see cref="ApplyLogVisibility"/> so it always matches
    /// the current state (even when the console was hidden from the header context menu).</summary>
    private MenuItem CreateLogMenuItem()
    {
        var item = new MenuItem
        {
            Header = LanguageService.GetString(_logVisible ? "Log.Hide" : "Log.Show"),
            Icon = new TextBlock
            {
                Text = _logVisible ? "✓" : " ",
                MinWidth = 16,
                TextAlignment = TextAlignment.Center,
            },
        };
        item.Click += (_, _) => SetLogVisible(!_logVisible);
        _viewLogMenuItem = item;
        return item;
    }

    /// <summary>Mirrors the current visibility into the checkmark icons of the
    /// Configuration → View submenu, so hiding a column (from anywhere) is reflected
    /// when the menu is re-opened. Mutates the live items in place — no rebuild.</summary>
    private void UpdateViewColumnChecks()
    {
        if (_viewColumnMenuItems is null)
            return;
        foreach (var item in _viewColumnMenuItems)
        {
            if (item is MenuItem { Tag: int id } mi && mi.Icon is TextBlock icon)
                icon.Text = _planColumns.ColumnById(id)?.Visible == true ? "✓" : " ";
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
        var picker = new SkyPickerWindow(textures, row.SkyName ?? "sky1",
            row.EnableSky2, row.Sky2Name, row.SkyScroll, row.Sky2Scroll);
        picker.SetViewModel(_viewModel);
        await picker.ShowDialog(this);
        if (picker.Accepted)
        {
            row.SkyName = picker.SelectedSkyName;
            row.Sky2Name = picker.SelectedSky2Name;
            row.EnableSky2 = picker.Sky2Enabled && picker.SelectedSky2Name.Length > 0;
            row.SkyScroll = picker.SkyScroll;
            row.Sky2Scroll = picker.Sky2Scroll;
        }
    }

    /// <summary>Opens the notes editor for the slot whose row button was clicked.</summary>
    private async void OnEditNotes(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SlotRowViewModel row)
            return;

        var notes = new NotesWindow(row.SlotName, row.Notes);
        await notes.ShowDialog(this);
        if (notes.Accepted)
            row.Notes = notes.Notes;
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

    private void OnThemeSystem(object? sender, RoutedEventArgs e) => ApplyTheme(ThemeService.System);

    private void OnThemeLight(object? sender, RoutedEventArgs e) => ApplyTheme(ThemeService.Light);

    private void OnThemeDark(object? sender, RoutedEventArgs e) => ApplyTheme(ThemeService.Dark);

    /// <summary>Switches the theme, refreshes the menu checkmark and persists the choice.</summary>
    private void ApplyTheme(string theme)
    {
        ThemeService.SetTheme(theme);
        UpdateThemeChecks();
        SaveConfigNow();
    }

    /// <summary>Marks the active theme with a checkmark in the Configuration → Theme menu.</summary>
    private void UpdateThemeChecks()
    {
        string current = ThemeService.CurrentTheme;
        if (_themeCheckSystem is { } system) system.Text = current == ThemeService.System ? "✓" : " ";
        if (_themeCheckLight is { } light) light.Text = current == ThemeService.Light ? "✓" : " ";
        if (_themeCheckDark is { } dark) dark.Text = current == ThemeService.Dark ? "✓" : " ";
    }

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