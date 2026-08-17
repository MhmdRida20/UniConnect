using Microsoft.Maui.Controls.Shapes;

namespace UniConnect.Mobile.Controls;

/// <summary>
/// The shared pieces every ride list card is built from.
/// </summary>
/// <remarks>
/// Both the Rides list and My Rides render the same kind of card, and both had
/// grown their own copy of it, so a fix to one silently missed the other. They
/// now build from here.
///
/// Two things this exists to get right:
///
/// 1. The status chip is a Border with a real corner radius, NOT a Frame with
///    CornerRadius=999. WinUI draws a radius larger than half the height as a
///    full ellipse, which is why those chips rendered as ovals with the text
///    clipped out of them. (Frame is also obsolete as of .NET 9.)
///
/// 2. Locations are whatever the driver typed or the geocoder returned, which
///    in practice can be a 120-character postal address mixing Arabic and Latin
///    script. Rendered as one "from -> to" label it produced a six-line block
///    that buried the date, the seats and the status. Each place gets its own
///    line, capped at one line and truncated, so the card keeps a fixed shape
///    no matter what is in it.
/// </remarks>
public static class RideCardParts
{
    private static Style Res(string key) => (Style)Application.Current!.Resources[key];

    private static View Dot(string colour, bool hollow) => new Border
    {
        WidthRequest = 11,
        HeightRequest = 11,
        BackgroundColor = hollow ? Colors.Transparent : Color.FromArgb(colour),
        Stroke = new SolidColorBrush(Color.FromArgb(colour)),
        StrokeThickness = hollow ? 2.5 : 0,
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
        VerticalOptions = LayoutOptions.Center,
        HorizontalOptions = LayoutOptions.Center
    };

    private static Label Place(string text) => new()
    {
        Text = string.IsNullOrWhiteSpace(text) ? "—" : text.Trim(),
        FontSize = 14.5,
        FontFamily = "MontserratSemiBold",
        TextColor = Color.FromArgb("#0f172a"),
        LineBreakMode = LineBreakMode.TailTruncation,
        MaxLines = 1,
        VerticalOptions = LayoutOptions.Center
    };

    /// <summary>Origin and destination as two truncated lines joined by a connector.</summary>
    public static View RouteBlock(string from, string to)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(12)),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };

        grid.Add(Dot("#16a34a", hollow: true), 0, 0);
        grid.Add(new BoxView
        {
            WidthRequest = 2,
            Color = Color.FromArgb("#d7e5dc"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Fill
        }, 0, 1);
        grid.Add(Dot("#0f7a46", hollow: false), 0, 2);

        grid.Add(Place(from), 1, 0);
        grid.Add(Place(to), 1, 2);
        return grid;
    }

    public static View StatusPill(string status, string bg, string fg) => new Border
    {
        Style = Res("UcStatusPill"),
        BackgroundColor = Color.FromArgb(bg),
        Content = new Label
        {
            Text = status,
            Style = Res("UcStatusPillText"),
            TextColor = Color.FromArgb(fg)
        }
    };

    /// <summary>
    /// A complete tappable card: route, optional status chip, and a meta row.
    /// </summary>
    public static Border Card(string from, string to,
                              (string Status, string Bg, string Fg)? status,
                              string metaLeft, string metaRight,
                              Func<Task> onTap)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
            RowSpacing = 14
        };

        grid.Add(RouteBlock(from, to), 0, 0);
        if (status is { } s)
            grid.Add(StatusPill(s.Status, s.Bg, s.Fg), 1, 0);

        var meta = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10
        };
        meta.Add(new Label
        {
            Text = metaLeft,
            Style = Res("UcTiny"),
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);
        meta.Add(new Label
        {
            Text = metaRight,
            Style = Res("UcTiny"),
            TextColor = Color.FromArgb("#0f7a46"),
            FontFamily = "MontserratSemiBold",
            LineBreakMode = LineBreakMode.NoWrap,
            VerticalOptions = LayoutOptions.Center
        }, 1, 0);
        Grid.SetColumnSpan(meta, 2);
        grid.Add(meta, 0, 1);

        var card = new Border { Style = Res("UcCardSoft"), Content = grid };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await onTap();
        card.GestureRecognizers.Add(tap);
        return card;
    }
}
