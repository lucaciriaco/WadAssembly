using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace WadAssembly.App.Resources;

/// <summary>Spanish (default) UI strings resource dictionary.</summary>
public partial class Strings_es : ResourceDictionary
{
    public Strings_es()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}