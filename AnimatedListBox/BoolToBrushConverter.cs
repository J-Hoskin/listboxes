using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace YourApp.Converters;

/// <summary>
/// Converts a bool to one of two brushes. Used to drive selection highlight
/// from IsSelected on the item wrapper VM rather than ListBox's built-in selection.
///
/// Usage in App.axaml resources:
///   &lt;converters:BoolToBrushConverter x:Key="SelectionBackgroundConverter"
///                                     TrueValue="#3a5a8a"
///                                     FalseValue="#222222" /&gt;
///
/// Usage in template:
///   Background="{Binding IsSelected, Converter={StaticResource SelectionBackgroundConverter}}"
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    public IBrush TrueValue { get; set; } = Brushes.Transparent;
    public IBrush FalseValue { get; set; } = Brushes.Transparent;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueValue : FalseValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
