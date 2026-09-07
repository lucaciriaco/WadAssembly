using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CommunityWadCompiler.App.Resources;

/// <summary>English UI strings resource dictionary.</summary>
public partial class Strings_en : ResourceDictionary
{
    public Strings_en()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}