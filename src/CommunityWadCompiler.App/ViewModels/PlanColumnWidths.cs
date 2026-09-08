using System.ComponentModel;
using Avalonia.Controls;

namespace CommunityWadCompiler.App.ViewModels;

/// <summary>Shared column widths of the slot plan grid. The header row and every slot
/// row bind their <c>ColumnDefinition.Width</c> to these properties so that dragging a
/// <c>GridSplitter</c> in the header keeps all rows aligned.</summary>
public sealed class PlanColumnWidths : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private GridLength _c0 = new(90);
    private GridLength _c1 = new(140);
    private GridLength _c2 = new(100);
    private GridLength _c3 = new(170);
    private GridLength _c4 = new(100);
    private GridLength _c5 = new(160);
    private GridLength _c6 = new(130);
    private GridLength _c7 = new(100);
    private GridLength _c8 = new(150);

    public GridLength C0 { get => _c0; set { _c0 = value; OnPropertyChanged(nameof(C0)); } }
    public GridLength C1 { get => _c1; set { _c1 = value; OnPropertyChanged(nameof(C1)); } }
    public GridLength C2 { get => _c2; set { _c2 = value; OnPropertyChanged(nameof(C2)); } }
    public GridLength C3 { get => _c3; set { _c3 = value; OnPropertyChanged(nameof(C3)); } }
    public GridLength C4 { get => _c4; set { _c4 = value; OnPropertyChanged(nameof(C4)); } }
    public GridLength C5 { get => _c5; set { _c5 = value; OnPropertyChanged(nameof(C5)); } }
    public GridLength C6 { get => _c6; set { _c6 = value; OnPropertyChanged(nameof(C6)); } }
    public GridLength C7 { get => _c7; set { _c7 = value; OnPropertyChanged(nameof(C7)); } }
    public GridLength C8 { get => _c8; set { _c8 = value; OnPropertyChanged(nameof(C8)); } }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}