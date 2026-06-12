using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JLNotes.Converters;

/// <summary>
/// Single source of truth for tag colors: the palette and the stable
/// tag-to-color mapping shared by every tag converter.
/// </summary>
internal static class TagPalette
{
    private static readonly Color[] Colors =
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

    /// <summary>Deterministic color for a tag: same tag -> same color across runs.</summary>
    public static Color ColorFor(string tag)
        => Colors[Math.Abs(StableHash(tag)) % Colors.Length];

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

/// <summary>Translucent background fill for a tag chip.</summary>
public class TagColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string tag || string.IsNullOrWhiteSpace(tag))
            return Brushes.Gray;

        var color = TagPalette.ColorFor(tag);
        return new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Full-strength text/foreground color for a tag chip.</summary>
public class TagTextColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string tag || string.IsNullOrWhiteSpace(tag))
            return Brushes.Gray;

        return new SolidColorBrush(TagPalette.ColorFor(tag));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
