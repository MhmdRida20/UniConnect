using System.Globalization;

namespace UniConnect.Mobile.Services;

/// <summary>
/// Turns a 0–1 proportion into a star <see cref="GridLength"/>.
///
/// This is how the seats bar is drawn: a two-column Grid weighted by taken and
/// remaining, with the filled column painted. MAUI's ProgressBar is the obvious
/// control for it, but its height and corner radius cannot be set consistently
/// across platforms — Android renders a fixed-height Material bar and ignores
/// HeightRequest — so it can never match the thick rounded bar in the design.
/// </summary>
public sealed class StarLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var proportion = value is double d ? d : 0;

        // GridLength rejects negatives, and a NaN silently collapses the row.
        if (double.IsNaN(proportion) || proportion < 0) proportion = 0;

        return new GridLength(proportion, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
