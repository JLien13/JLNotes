using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace JLNotes.Converters;

// Visible when the bound value is non-null; Collapsed when null.
// Pass ConverterParameter="invert" to flip it (Visible when null) — used for
// the "select a note" empty state in the split detail pane.
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null;
        var invert = parameter as string == "invert";
        var visible = invert ? isNull : !isNull;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
