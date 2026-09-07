using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CommunityWadCompiler.App.ViewModels;

namespace CommunityWadCompiler.App.Views;

public partial class MusicPickerWindow : Window
{
    /// <summary>True when the selection was confirmed with Aceptar.</summary>
    public bool Accepted { get; private set; }

    /// <summary>Lump name of the chosen music, or SlotRowViewModel.NoMusicOption.</summary>
    public string SelectedName { get; private set; } = "";

    /// <summary>Absolute path of an external music file when the user chose "Cargar música externa". Empty otherwise.</summary>
    public string SelectedExternalPath { get; private set; } = "";

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
        SelectedExternalPath = "";
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnPickExternal(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar archivo de música externo",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Archivos de música") { Patterns = new[] { "*.mid", "*.mod", "*.it", "*.xm", "*.s3m" } },
                new FilePickerFileType("MIDI") { Patterns = new[] { "*.mid" } },
                new FilePickerFileType("MOD (ProTracker)") { Patterns = new[] { "*.mod" } },
                new FilePickerFileType("Impulse Tracker") { Patterns = new[] { "*.it" } },
            },
        });

        if (files.Count == 0)
            return;

        string filePath = files[0].Path.LocalPath;

        // Derive a WAD lump name (8 chars max, uppercase, alphanumeric only).
        string raw = Path.GetFileNameWithoutExtension(filePath).ToUpperInvariant();
        string lumpName = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '_').Take(8).ToArray());
        if (lumpName.Length == 0)
            lumpName = "CUSTOM";

        // Avoid collision with existing music candidates.
        var musicList = this.FindControl<ListBox>("MusicList")!;
        var existing = musicList.ItemsSource as IReadOnlyList<MusicLumpInfo>;
        if (existing is not null)
        {
            while (existing.Any(o => string.Equals(o.Name, lumpName, StringComparison.OrdinalIgnoreCase)))
            {
                if (lumpName.Length < 8)
                    lumpName += (char)('0' + (lumpName.Count(char.IsDigit) % 10));
                else
                    lumpName = lumpName[..7] + (char)('0' + (lumpName.Count(char.IsDigit) % 10));
            }
        }

        SelectedName = lumpName;
        SelectedExternalPath = filePath;
        Accepted = true;
        Close();
    }
}