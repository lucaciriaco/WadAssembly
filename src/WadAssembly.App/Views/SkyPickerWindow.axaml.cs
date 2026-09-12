using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using WadAssembly.App.Services;
using WadAssembly.App.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace WadAssembly.App.Views;

public partial class SkyPickerWindow : Window
{
    /// <summary>True when the selection was confirmed with Aceptar.</summary>
    public bool Accepted { get; private set; }

    /// <summary>Selected sky texture name.</summary>
    public string SelectedSkyName { get; private set; } = "sky1";

    /// <summary>Selected second sky texture name; empty when sky2 is disabled.</summary>
    public string SelectedSky2Name { get; private set; } = "";

    /// <summary>Horizontal rotation speed of the sky (0 = static).</summary>
    public double SkyScroll { get; private set; }

    /// <summary>Horizontal rotation speed of the sky2 layer (0 = static).</summary>
    public double Sky2Scroll { get; private set; }

    /// <summary>True when the user enabled the sky2 layer.</summary>
    public bool Sky2Enabled { get; private set; }

    private readonly List<string> _allTextures;
    private MainWindowViewModel? _viewModel;
    private byte[]? _palette;

    /// <summary>Required by the Avalonia runtime loader; use the parameterized constructor.</summary>
    public SkyPickerWindow()
    {
        InitializeComponent();
        _allTextures = new List<string> { "sky1" };
    }

    public SkyPickerWindow(IReadOnlyList<string> textures, string currentSky,
        bool sky2Enabled = false, string? currentSky2 = null, double skyScroll = 0, double sky2Scroll = 0)
    {
        InitializeComponent();

        _allTextures = textures.ToList();

        var textureList = this.FindControl<ListBox>("TextureList")!;
        var manualSkyBox = this.FindControl<TextBox>("ManualSkyBox")!;
        var searchBox = this.FindControl<TextBox>("SearchBox")!;
        var rotSpeedBox = this.FindControl<TextBox>("RotSpeedBox")!;
        var sky2Check = this.FindControl<CheckBox>("Sky2Check")!;
        var sky2NameBox = this.FindControl<TextBox>("Sky2NameBox")!;
        var sky2RotBox = this.FindControl<TextBox>("Sky2RotBox")!;

        textureList.ItemsSource = _allTextures;

        // Set current sky
        manualSkyBox.Text = currentSky;
        SelectedSkyName = currentSky;

        // Sky rotation speed + sky2 layer
        rotSpeedBox.Text = skyScroll.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        SkyScroll = skyScroll;
        sky2Check.IsChecked = sky2Enabled;
        Sky2Enabled = sky2Enabled;
        sky2NameBox.Text = currentSky2 ?? "";
        SelectedSky2Name = sky2Enabled ? (currentSky2 ?? "") : "";
        sky2RotBox.Text = sky2Scroll.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        Sky2Scroll = sky2Scroll;
        UpdateSky2Visibility();

        // Select in list if present
        var match = _allTextures.FirstOrDefault(t => string.Equals(t, currentSky, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            textureList.SelectedItem = match;

        UpdatePreview(currentSky);
    }

    public void SetViewModel(MainWindowViewModel vm)
    {
        _viewModel = vm;
        _palette = vm.TryGetPalette();
        // Refresh the initial preview now that the palette/model is available.
        UpdatePreview(SelectedSkyName);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSky2Toggled(object? sender, RoutedEventArgs e)
    {
        Sky2Enabled = (sender as CheckBox)?.IsChecked == true;
        UpdateSky2Visibility();
    }

    private void UpdateSky2Visibility()
    {
        var sky2Label = this.FindControl<TextBlock>("Sky2Label")!;
        var sky2NameBox = this.FindControl<TextBox>("Sky2NameBox")!;
        var sky2RotLabel = this.FindControl<TextBlock>("Sky2RotLabel")!;
        var sky2RotBox = this.FindControl<TextBox>("Sky2RotBox")!;
        sky2Label.IsVisible = Sky2Enabled;
        sky2NameBox.IsVisible = Sky2Enabled;
        sky2NameBox.IsEnabled = Sky2Enabled;
        sky2NameBox.IsHitTestVisible = Sky2Enabled;
        sky2RotLabel.IsVisible = Sky2Enabled;
        sky2RotBox.IsVisible = Sky2Enabled;
        sky2RotBox.IsEnabled = Sky2Enabled;
        sky2RotBox.IsHitTestVisible = Sky2Enabled;
    }

    private void OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        Accepted = true;
        ReadControls();
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

        var bitmap = TexturePreviewDecoder.Decode(data, skyName, _palette);
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
        Accepted = true;
        ReadControls();
        Close();
    }

    private void ReadControls()
    {
        var manualSkyBox = this.FindControl<TextBox>("ManualSkyBox")!;
        var rotSpeedBox = this.FindControl<TextBox>("RotSpeedBox")!;
        var sky2Check = this.FindControl<CheckBox>("Sky2Check")!;
        var sky2NameBox = this.FindControl<TextBox>("Sky2NameBox")!;
        var sky2RotBox = this.FindControl<TextBox>("Sky2RotBox")!;

        SelectedSkyName = manualSkyBox.Text?.Trim() ?? "sky1";

        Sky2Enabled = sky2Check.IsChecked == true;
        string sky2 = sky2NameBox.Text?.Trim() ?? "";
        SelectedSky2Name = Sky2Enabled && sky2.Length > 0 ? sky2 : "";

        SkyScroll = ParseSpeed(rotSpeedBox.Text);
        Sky2Scroll = Sky2Enabled ? ParseSpeed(sky2RotBox.Text) : 0;
    }

    private static double ParseSpeed(string? text)
    {
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double speed)
            || double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out speed))
        {
            return speed;
        }
        return 0;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}