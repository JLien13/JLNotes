using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JLNotes.Converters;

public class TagColorConverter : IValueConverter
{
    private static readonly Color[] TagColors =
    [
        Color.FromRgb(0x4A, 0x9E, 0xFF), // blue
        Color.FromRgb(0x34, 0xD3, 0x99), // green
        Color.FromRgb(0xF5, 0x9E, 0x0B), // amber
        Color.FromRgb(0xA7, 0x8B, 0xFA), // purple
        Color.FromRgb(0xEC, 0x48, 0x99), // pink
        Color.FromRgb(0x06, 0xB6, 0xD4), // cyan
        Color.FromRgb(0x84, 0xCC, 0x16), // lime
        Color.FromRgb(0xEF, 0x44, 0x44), // red
    ];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string tag || string.IsNullOrWhiteSpace(tag))
            return Brushes.Gray;

        var index = Math.Abs(StableHash(tag)) % TagColors.Length;
        var color = TagColors[index];
        return new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static int StableHash(string s)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in s)
                hash = hash * 31 + c;
            return hash;
        }
    }
}

public class TagTextColorConverter : IValueConverter
{
    private static readonly Color[] TagColors =
    [
        Color.FromRgb(0x4A, 0x9E, 0xFF),
        Color.FromRgb(0x34, 0xD3, 0x99),
        Color.FromRgb(0xF5, 0x9E, 0x0B),
        Color.FromRgb(0xA7, 0x8B, 0xFA),
        Color.FromRgb(0xEC, 0x48, 0x99),
        Color.FromRgb(0x06, 0xB6, 0xD4),
        Color.FromRgb(0x84, 0xCC, 0x16),
        Color.FromRgb(0xEF, 0x44, 0x44),
    ];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string tag || string.IsNullOrWhiteSpace(tag))
            return Brushes.Gray;

        var index = Math.Abs(StableHash(tag)) % TagColors.Length;
        return new SolidColorBrush(TagColors[index]);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static int StableHash(string s)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in s)
                hash = hash * 31 + c;
            return hash;
        }
    }
}
