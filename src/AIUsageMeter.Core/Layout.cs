namespace AIUsageMeter.Core;

public readonly record struct MeterRect(double X, double Y, double Width, double Height)
{
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidY => Y + Height / 2;
}

public static class OverlayLayout
{
    /// <summary>Distance from the work-area edge when the overlay is pinned top or bottom.</summary>
    public const double EdgeInset = 40;

    /// <summary>Shortest overlay the rail is ever given, however few providers are visible.</summary>
    public const double MinimumHeight = 116;

    public static MeterRect Place(MeterRect workArea, double width, double height, VerticalPosition position, double offset)
    {
        var placed = Math.Min(Math.Max(MinimumHeight, height), workArea.Height);
        var baseY = position switch
        {
            VerticalPosition.Top => workArea.Y + EdgeInset,
            VerticalPosition.Bottom => workArea.Y + workArea.Height - placed - EdgeInset,
            _ => workArea.Y + (workArea.Height - placed) / 2
        };
        return new(workArea.X + Math.Max(0, workArea.Width - width), Clamp(workArea, baseY + offset, placed), width, placed);
    }

    /// <summary>
    /// The window holding rail and card together. The card can be taller than the rail, so the panel
    /// grows around the rail's midpoint rather than re-deriving its own position — otherwise the rail
    /// would slide up the screen every time a tall card opened.
    /// </summary>
    public static MeterRect PanelFrame(MeterRect workArea, double width, double railHeight, double panelHeight, VerticalPosition position, double offset)
    {
        var rail = Place(workArea, width, railHeight, position, offset);
        var height = Math.Min(Math.Max(panelHeight, rail.Height), workArea.Height);
        return new(workArea.X + Math.Max(0, workArea.Width - width), Clamp(workArea, rail.MidY - height / 2, height), width, height);
    }

    /// <summary>The idle tab, centred on wherever the rail would have been.</summary>
    public static MeterRect MiniFrame(MeterRect workArea, double width, double height, double railHeight, VerticalPosition position, double offset)
    {
        var rail = Place(workArea, width, railHeight, position, offset);
        var tab = Math.Min(height, workArea.Height);
        return new(workArea.X + Math.Max(0, workArea.Width - width), Clamp(workArea, rail.MidY - tab / 2, tab), width, tab);
    }

    /// <summary>
    /// Where the detail card sits, given the gauge it belongs to.
    /// </summary>
    /// <remarks>
    /// <paramref name="gaugeCentre"/> and the returned centre are offsets from the middle of the panel;
    /// the returned tail centre is measured down from the top of the card. The card is kept inside the
    /// panel, and the tail then takes up whatever slack that clamping introduced so it still points at
    /// its own gauge.
    /// </remarks>
    public static (double Centre, double TailCentre) CardPlacement(
        double gaugeCentre, double cardHeight, double available, double tailInset, double margin = 6)
    {
        var room = Math.Max(0, available / 2 - cardHeight / 2 - margin);
        var centre = Math.Clamp(gaugeCentre, -room, room);
        var ideal = cardHeight / 2 + (gaugeCentre - centre);
        // A card shorter than two insets collapses both bounds onto the same point rather than crossing.
        var lowest = Math.Min(tailInset, cardHeight / 2);
        var highest = Math.Max(cardHeight - tailInset, lowest);
        return (centre, Math.Clamp(ideal, lowest, highest));
    }

    private static double Clamp(MeterRect workArea, double y, double height)
        => Math.Clamp(y, workArea.Y, workArea.Y + Math.Max(0, workArea.Height - height));
}

public static class SupportLinks
{
    public static readonly Uri Repository = new("https://github.com/dngkec/aiusagemeter");
    public static readonly Uri Issues = new("https://github.com/dngkec/aiusagemeter/issues");
    public static readonly Uri Sponsor = new("https://buymeacoffee.com/dngkec");
    public static readonly Uri Designer = new("https://x.com/hivinz_");
    public const string SponsorLabel = "Buy me a coffee";
    public const string RepositoryLabel = "View on GitHub";
    public const string DesignerHandle = "@hivinz_";
    public const string SponsorBlurb = "AIUsageMeter is free and open source. A coffee keeps it going.";
    public static bool IsAllowed(Uri uri) => new[] { Repository, Issues, Sponsor, Designer }.Any(x => x == uri);
}
