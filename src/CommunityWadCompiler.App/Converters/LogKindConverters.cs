using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityWadCompiler.App.ViewModels;

namespace CommunityWadCompiler.App.Converters;

/// <summary>Asigna un <see cref="LogEntryKind"/> a un color de consola. Las líneas
/// normales devuelven null para heredar el color por defecto del tema.</summary>
public sealed class LogKindColorConverter : IValueConverter
{
    private static readonly Brush Error = new SolidColorBrush(Color.Parse("#E53935"));
    private static readonly Brush Warning = new SolidColorBrush(Color.Parse("#EF6C00"));
    private static readonly Brush Info = new SolidColorBrush(Color.Parse("#1E88E5"));
    private static readonly Brush Success = new SolidColorBrush(Color.Parse("#43A047"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            LogEntryKind.Error => Error,
            LogEntryKind.Warning => Warning,
            LogEntryKind.Info => Info,
            LogEntryKind.Success => Success,
            _ => null,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Asigna un <see cref="LogEntryKind"/> a un peso de fuente; los colores van en negrita.</summary>
public sealed class LogKindWeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LogEntryKind.Plain ? FontWeight.Normal : FontWeight.Bold;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}