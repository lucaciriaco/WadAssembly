using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using CommunityWadCompiler.App.Services;
using CommunityWadCompiler.App.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace CommunityWadCompiler.App.Views;

public partial class SkyPickerWindow : Window
{
    /// <summary>True when the selection was confirmed with Aceptar.</summary>
    public bool Accepted { get; private set; }

    /// <summary>Selected sky texture name.</summary>
    public string SelectedSkyName { get; private set; } = "sky1";

    private readonly List<string> _allTextures;
    private MainWindowViewModel? _viewModel;

    /// <summary>Required by the Avalonia runtime loader; use the parameterized constructor.</summary>
    public SkyPickerWindow()
    {
        InitializeComponent();
        _allTextures = new List<string> { "sky1" };
    }

    public SkyPickerWindow(IReadOnlyList<string> textures, string currentSky)
    {
        InitializeComponent();

        _allTextures = textures.ToList();

        var textureList = this.FindControl<ListBox>("TextureList")!;
        var manualSkyBox = this.FindControl<TextBox>("ManualSkyBox")!;
        var searchBox = this.FindControl<TextBox>("SearchBox")!;

        textureList.ItemsSource = _allTextures;

        // Set current sky
        manualSkyBox.Text = currentSky;
        SelectedSkyName = currentSky;

        // Select in list if present
        var match = _allTextures.FirstOrDefault(t => string.Equals(t, currentSky, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            textureList.SelectedItem = match;

        UpdatePreview(currentSky);
    }

    public void SetViewModel(MainWindowViewModel vm) => _viewModel = vm;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        Accepted = true;
        var manualSkyBox = this.FindControl<TextBox>("ManualSkyBox")!;
        SelectedSkyName = manualSkyBox.Text?.Trim() ?? "sky1";
        Close();
    }

    private void OnSearchChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        var searchBox = this.FindControl<TextBox>("SearchBox")!;
        var textureList = this.FindControl<ListBox>("TextureList")!;
        
        var filter = searchBox.Text?.ToLowerInvariant() ?? "";
        var filtered = _allTextures
            .Where(t => t.ToLowerInvariant().Contains(filter))
            .ToList();
        textureList.ItemsSource = filtered;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var textureList = this.FindControl<ListBox>("TextureList")!;
        var manualSkyBox = this.FindControl<TextBox>("ManualSkyBox")!;

        if (textureList.SelectedItem is string selected)
        {
            manualSkyBox.Text = selected;
            SelectedSkyName = selected;
            UpdatePreview(selected);
        }
    }

    private void UpdatePreview(string skyName)
    {
        var previewImage = this.FindControl<Image>("PreviewImage")!;
        var previewInfo = this.FindControl<TextBlock>("PreviewInfo")!;

        if (_viewModel is null)
        {
            previewInfo.Text = LanguageService.GetString("SkyPicker.PreviewNotAvailable");
            previewImage.Source = null;
            return;
        }

        var data = _viewModel.TryGetTextureData(skyName);
        if (data is null)
        {
            previewInfo.Text = string.Format(LanguageService.GetString("SkyPicker.NotFound"), skyName);
            previewImage.Source = null;
            return;
        }

        var bitmap = TexturePreviewDecoder.Decode(data, skyName);
        if (bitmap is null)
        {
            previewInfo.Text = string.Format(LanguageService.GetString("SkyPicker.UnsupportedFormat"), skyName, data.Length);
            previewImage.Source = null;
            return;
        }

        previewImage.Source = bitmap;
        previewInfo.Text = $"{skyName} — {data.Length} bytes";
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        var manualSkyBox = this.FindControl<TextBox>("ManualSkyBox")!;
        Accepted = true;
        SelectedSkyName = manualSkyBox.Text?.Trim() ?? "sky1";
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}