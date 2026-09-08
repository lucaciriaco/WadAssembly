using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls;
using CommunityWadCompiler.App.Models;

namespace CommunityWadCompiler.App.ViewModels;

/// <summary>State of the plan-sheet columns (set, order, width, visibility) in UI
/// order. Widths and visibility are per-column, so a column keeps its width when it
/// is hidden, re-shown or moved. Physical grid columns are derived from this state
/// via <see cref="ApplyTo"/>.</summary>
public sealed class PlanColumnsViewModel
{
    public static readonly IReadOnlyList<(int Id, string TitleKey, double Width)> Presets =
        new (int, string, double)[]
        {
            (0, "Header.Slot", 90),
            (1, "Header.Wad", 140),
            (2, "Header.OriginalMap", 100),
            (3, "Header.LevelName", 170),
            (4, "Header.Status", 100),
            (5, "Header.LastModified", 160),
            (6, "Header.Music", 130),
            (7, "Header.Sky", 100),
            (8, "Header.Author", 150),
        };

    public ObservableCollection<PlanColumnViewModel> Columns { get; } = new();

    /// <summary>Raised whenever the set, order, width or visibility of a column changes
    /// (e.g. after a reorder, a splitter drag or a visibility toggle).</summary>
    public event Action? ColumnsChanged;

    private bool _suppressChanges;

    public PlanColumnsViewModel()
    {
        foreach (var (id, titleKey, width) in Presets)
        {
            var column = new PlanColumnViewModel(id, titleKey, width);
            column.PropertyChanged += (_, _) => NotifyChanges();
            Columns.Add(column);
        }
        Columns.CollectionChanged += OnColumnsCollectionChanged;
    }

    public PlanColumnViewModel? ColumnById(int id) => Columns.FirstOrDefault(c => c.Id == id);

    public PlanColumnViewModel ColumnAt(int index) => Columns[index];

    public int IndexOfId(int id)
    {
        for (int i = 0; i < Columns.Count; i++)
            if (Columns[i].Id == id)
                return i;
        return -1;
    }

    /// <summary>Column ids in current UI order (9 values).</summary>
    public int[] Order => Columns.Select(c => c.Id).ToArray();

    /// <summary>Moves the column at <paramref name="from"/> to <paramref name="to"/>.
    /// The column keeps its width; the physical grid is updated by the change event.</summary>
    public void Move(int from, int to) => Columns.Move(from, to);

    /// <summary>Derives the physical grid widths and minimums from this state: hidden
    /// columns collapse to 0 (width and minimum), visible columns use their width.</summary>
    public void ApplyTo(PlanColumnWidths plan)
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];
            plan.SetWidth(i, new GridLength(column.Visible ? column.Width : 0));
            plan.SetMin(i, column.Visible ? PlanColumnWidths.DefaultMinWidths[i] : 0);
        }
    }

    /// <summary>Restores widths, order and visibility persisted in the settings. Change
    /// notifications are deferred until the whole state is consistent (the columns are
    /// temporarily cleared while the saved order is applied).</summary>
    public void LoadFrom(AppSettings settings)
    {
        _suppressChanges = true;
        try
        {
            if (IsValidColumnOrder(settings.ColumnOrder))
                ApplyOrder(settings.ColumnOrder!);

            if (settings.ColumnWidths is { Length: 9 } widths)
            {
                for (int i = 0; i < 9; i++)
                {
                    var column = Columns[i];
                    if (widths[i] > 0)
                        column.Width = widths[i];
                }
            }

            if (settings.ColumnVisibility is { Length: 9 } visibility)
            {
                for (int i = 0; i < 9; i++)
                    Columns[i].Visible = visibility[i];
            }
            else
            {
                foreach (var column in Columns)
                    column.Visible = true;
            }
        }
        finally
        {
            _suppressChanges = false;
        }
        ColumnsChanged?.Invoke();
    }

    /// <summary>Persists the current widths, order and visibility into the settings.</summary>
    public void SaveTo(AppSettings settings)
    {
        settings.ColumnWidths = Columns.Select(c => c.Width).ToArray();
        settings.ColumnOrder = Order;
        settings.ColumnVisibility = Columns.Select(c => c.Visible).ToArray();
    }

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => NotifyChanges();

    private void NotifyChanges()
    {
        if (!_suppressChanges)
            ColumnsChanged?.Invoke();
    }

    private void ApplyOrder(int[] order)
    {
        var reordered = order.Select(ColumnById)
            .Where(c => c is not null)
            .Cast<PlanColumnViewModel>()
            .ToList();
        if (reordered.Count != 9)
            return;
        Columns.Clear();
        foreach (var column in reordered)
            Columns.Add(column);
    }

    private static bool IsValidColumnOrder(int[]? order)
        => order is { Length: 9 } && order.Distinct().Count() == 9 && order.All(i => i is >= 0 and <= 8);
}