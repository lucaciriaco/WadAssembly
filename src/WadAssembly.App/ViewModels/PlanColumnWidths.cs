using System.ComponentModel;
using Avalonia.Controls;

namespace WadAssembly.App.ViewModels;

/// <summary>Shared column widths of the slot plan grid. The header row and every slot
/// row bind their <c>ColumnDefinition.Width</c> to these properties so that dragging a
/// <c>GridSplitter</c> in the header keeps all rows aligned.</summary>
public sealed class PlanColumnWidths : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private GridLength _c0 = new(PlanColumnsViewModel.Presets[0].Width);
    private GridLength _c1 = new(PlanColumnsViewModel.Presets[1].Width);
    private GridLength _c2 = new(PlanColumnsViewModel.Presets[2].Width);
    private GridLength _c3 = new(PlanColumnsViewModel.Presets[3].Width);
    private GridLength _c4 = new(PlanColumnsViewModel.Presets[4].Width);
    private GridLength _c5 = new(PlanColumnsViewModel.Presets[5].Width);
    private GridLength _c6 = new(PlanColumnsViewModel.Presets[6].Width);
    private GridLength _c7 = new(PlanColumnsViewModel.Presets[7].Width);
    private GridLength _c8 = new(PlanColumnsViewModel.Presets[8].Width);
    private GridLength _c9 = new(PlanColumnsViewModel.Presets[9].Width);

    // Min widths per physical position for the default column order. The values follow
    // the columns that occupy each position in the baked default layout (see Presets):
    // Slot 60, Wad 80, OriginalMap 80, LevelName 110, LastModified 90, Music 110,
    // Sky 90, Author 120, Status 90, Notes 110.
    public static readonly double[] DefaultMinWidths = { 60, 80, 80, 110, 90, 110, 90, 120, 90, 110 };

    private double _min0 = DefaultMinWidths[0];
    private double _min1 = DefaultMinWidths[1];
    private double _min2 = DefaultMinWidths[2];
    private double _min3 = DefaultMinWidths[3];
    private double _min4 = DefaultMinWidths[4];
    private double _min5 = DefaultMinWidths[5];
    private double _min6 = DefaultMinWidths[6];
    private double _min7 = DefaultMinWidths[7];
    private double _min8 = DefaultMinWidths[8];
    private double _min9 = DefaultMinWidths[9];

    public GridLength C0 { get => _c0; set { _c0 = value; OnPropertyChanged(nameof(C0)); } }
    public GridLength C1 { get => _c1; set { _c1 = value; OnPropertyChanged(nameof(C1)); } }
    public GridLength C2 { get => _c2; set { _c2 = value; OnPropertyChanged(nameof(C2)); } }
    public GridLength C3 { get => _c3; set { _c3 = value; OnPropertyChanged(nameof(C3)); } }
    public GridLength C4 { get => _c4; set { _c4 = value; OnPropertyChanged(nameof(C4)); } }
    public GridLength C5 { get => _c5; set { _c5 = value; OnPropertyChanged(nameof(C5)); } }
    public GridLength C6 { get => _c6; set { _c6 = value; OnPropertyChanged(nameof(C6)); } }
    public GridLength C7 { get => _c7; set { _c7 = value; OnPropertyChanged(nameof(C7)); } }
    public GridLength C8 { get => _c8; set { _c8 = value; OnPropertyChanged(nameof(C8)); } }
    public GridLength C9 { get => _c9; set { _c9 = value; OnPropertyChanged(nameof(C9)); } }

    public double MinW0 { get => _min0; set { _min0 = value; OnPropertyChanged(nameof(MinW0)); } }
    public double MinW1 { get => _min1; set { _min1 = value; OnPropertyChanged(nameof(MinW1)); } }
    public double MinW2 { get => _min2; set { _min2 = value; OnPropertyChanged(nameof(MinW2)); } }
    public double MinW3 { get => _min3; set { _min3 = value; OnPropertyChanged(nameof(MinW3)); } }
    public double MinW4 { get => _min4; set { _min4 = value; OnPropertyChanged(nameof(MinW4)); } }
    public double MinW5 { get => _min5; set { _min5 = value; OnPropertyChanged(nameof(MinW5)); } }
    public double MinW6 { get => _min6; set { _min6 = value; OnPropertyChanged(nameof(MinW6)); } }
    public double MinW7 { get => _min7; set { _min7 = value; OnPropertyChanged(nameof(MinW7)); } }
    public double MinW8 { get => _min8; set { _min8 = value; OnPropertyChanged(nameof(MinW8)); } }
    public double MinW9 { get => _min9; set { _min9 = value; OnPropertyChanged(nameof(MinW9)); } }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Width of the physical column at <paramref name="index"/> (0..9).</summary>
    public GridLength GetWidth(int index) => index switch
    {
        0 => C0,
        1 => C1,
        2 => C2,
        3 => C3,
        4 => C4,
        5 => C5,
        6 => C6,
        7 => C7,
        8 => C8,
        9 => C9,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>Sets the width of the physical column at <paramref name="index"/> (0..9).</summary>
    public void SetWidth(int index, GridLength value)
    {
        switch (index)
        {
            case 0: C0 = value; break;
            case 1: C1 = value; break;
            case 2: C2 = value; break;
            case 3: C3 = value; break;
            case 4: C4 = value; break;
            case 5: C5 = value; break;
            case 6: C6 = value; break;
            case 7: C7 = value; break;
            case 8: C8 = value; break;
            case 9: C9 = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>Sets the minimum width of the physical column at <paramref name="index"/>
    /// (0..9). A value of 0 lets a hidden column collapse its slot entirely.</summary>
    public void SetMin(int index, double value)
    {
        switch (index)
        {
            case 0: MinW0 = value; break;
            case 1: MinW1 = value; break;
            case 2: MinW2 = value; break;
            case 3: MinW3 = value; break;
            case 4: MinW4 = value; break;
            case 5: MinW5 = value; break;
            case 6: MinW6 = value; break;
            case 7: MinW7 = value; break;
            case 8: MinW8 = value; break;
            case 9: MinW9 = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}