using Avalonia.Media;
using Avalonia.Data.Converters;

namespace CommunityWadCompiler.App.ViewModels;

/// <summary>Converts status string to color brush for dropdown display.</summary>
public sealed class StatusToColorConverter : IValueConverter
{
    public static readonly StatusToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "TODO" => Brushes.White,
            "WIP" => Brushes.Yellow,
            "DONE" => Brushes.LimeGreen,
            "FIX" => Brushes.MediumPurple,
            _ => Brushes.Transparent
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}