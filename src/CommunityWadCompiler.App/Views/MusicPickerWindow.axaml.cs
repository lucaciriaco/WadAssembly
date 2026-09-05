using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityWadCompiler.App.ViewModels;

namespace CommunityWadCompiler.App.Views;

public partial class MusicPickerWindow : Window
{
    /// <summary>True when the selection was confirmed with Aceptar.</summary>
    public bool Accepted { get; private set; }

    /// <summary>Lump name of the chosen music, or SlotRowViewModel.NoMusicOption.</summary>
    public string SelectedName { get; private set; } = "";

    /// <summary>Required by the Avalonia runtime loader; use the parameterized constructor.</summary>
    public MusicPickerWindow()
    {
        InitializeComponent();
    }

    public MusicPickerWindow(IReadOnlyList<MusicLumpInfo> options, string currentMusic)
    {
        InitializeComponent();

        var musicList = this.FindControl<ListBox>("MusicList")!;
        musicList.ItemsSource = options;
        string current = string.IsNullOrWhiteSpace(currentMusic) || currentMusic == SlotRowViewModel.NoMusicOption
            ? SlotRowViewModel.NoMusicOption
            : currentMusic;
        musicList.SelectedItem = options.FirstOrDefault(o => o.Name == current) ?? options.LastOrDefault();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        Accepted = true;
        var musicList = this.FindControl<ListBox>("MusicList")!;
        SelectedName = (musicList.SelectedItem as MusicLumpInfo)?.Name ?? "";
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}