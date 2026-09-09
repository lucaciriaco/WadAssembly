using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CommunityWadCompiler.App.Models;
using CommunityWadCompiler.App.Services;

namespace CommunityWadCompiler.App.Views;

/// <summary>Manages the ZDL-style source port list of the app config: add, edit
/// and remove ports (name, executable, extra arguments). On acceptance the list
/// replaces the stored one; Cancel discards every change.</summary>
public partial class SourcePortsWindow : Window
{
    /// <summary>True when the list was confirmed with Aceptar.</summary>
    public bool Accepted { get; private set; }

    private readonly List<SourcePortConfig> _working = new();

    public SourcePortsWindow()
    {
        InitializeComponent();
        _working.AddRange(AppSettingsService.Load().SourcePorts.Select(p => new SourcePortConfig
        {
            Name = p.Name,
            ExecutablePath = p.ExecutablePath,
            Arguments = p.Arguments,
        }));
        var list = this.FindControl<ListBox>("PortList")!;
        list.ItemsSource = _working;
        if (_working.Count > 0)
            list.SelectedIndex = 0;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ListBox PortListControl => this.FindControl<ListBox>("PortList")!;

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        var port = new SourcePortConfig();
        _working.Add(port);
        PortListControl.SelectedItem = port;
        PortListControl.ScrollIntoView(port);
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (PortListControl.SelectedItem is SourcePortConfig port)
            _working.Remove(port);
    }

    private async void OnBrowseExecutable(object? sender, RoutedEventArgs e)
    {
        if (PortListControl.SelectedItem is not SourcePortConfig port)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LanguageService.GetString("SourcePorts.Executable.Browse"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Executable") { Patterns = new[] { "*.exe" } } },
        });
        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            port.ExecutablePath = path;
    }

    private void OnClearExecutable(object? sender, RoutedEventArgs e)
    {
        if (PortListControl.SelectedItem is SourcePortConfig port)
            port.ExecutablePath = "";
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        var settings = AppSettingsService.Load();
        settings.SourcePorts = _working
            .Where(p => !string.IsNullOrWhiteSpace(p.ExecutablePath))
            .ToList();
        AppSettingsService.Save(settings);
        Accepted = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}