namespace UniConnect.Mobile.Services;

/// <summary>
/// Layout breakpoints, taken from Bootstrap's so the app reflows at the same
/// widths the web does.
///
/// This matters more than it looks: the Windows build runs in a freely
/// resizable desktop window, so a layout that only ever considers phone widths
/// leaves a single column of cards stranded in the middle of a 1600px window,
/// and stretches form inputs to a width nobody can scan.
/// </summary>
public static class Responsive
{
    public const double Sm = 576;
    public const double Lg = 992;
    public const double Xl = 1200;

    /// <summary>
    /// Caps how wide text and forms are allowed to grow. Long measures are
    /// hard to read, and a full-width login form on a desktop monitor looks
    /// broken rather than spacious.
    /// </summary>
    public const double ContentMaxWidth = 1120;

    public const double FormMaxWidth = 620;
    public const double LoginMaxWidth = 430;

    /// <summary>
    /// The narrowest a card may get before its content starts truncating —
    /// the progress bar, the member count and the creator name all have to fit
    /// on one row.
    /// </summary>
    public const double MinCardWidth = 380;

    /// <summary>
    /// Cards per row. Derived from how much room a card actually needs rather
    /// than from viewport breakpoints: the web's "col-sm-6" splits at 576px
    /// because that is a whole browser window, but this app runs in a desktop
    /// window that can BE 576px wide, and splitting one there left every card
    /// too narrow to show its own footer.
    /// </summary>
    public static int CardColumns(double width) =>
        Math.Clamp((int)(width / MinCardWidth), 1, 3);

    /// <summary>
    /// Whether there is room for the details page's two-column layout — the
    /// web's "col-lg-8 / col-lg-4" split.
    /// </summary>
    public static bool IsTwoColumn(double width) => width >= Lg;
}
