using AIUsageMeter.Core;

namespace AIUsageMeter.Windows.Design;

/// <summary>
/// Every measurement in the overlay, mirroring <c>Sources/AIUsageMeter/Design.swift</c>.
/// </summary>
/// <remarks>
/// The macOS original is a global whose <c>scale</c> is reassigned in place. An immutable instance
/// per size is the same arithmetic without the shared mutable state, and lets the two sizes be
/// compared side by side in a test.
/// <para>
/// Each metric is rounded after scaling, exactly as macOS does. Scaling the visual tree with a
/// transform instead is a different operation and would blur every hairline.
/// </para>
/// </remarks>
internal sealed class Metrics
{
    /// <summary>A hairline stays a hairline at every overlay size, so this one never scales.</summary>
    public const double Hairline = 1;

    private static readonly Metrics SmallSize = new(OverlaySize.Small.Scale());
    private static readonly Metrics MediumSize = new(OverlaySize.Medium.Scale());
    private static readonly Metrics LargeSize = new(OverlaySize.Large.Scale());

    private Metrics(double scale)
    {
        Scale = scale;

        RailWidth = Scaled(72);
        RailCorner = Scaled(44);
        RailPadding = Scaled(18);

        Gauge = Scaled(46);
        GaugeRing = Scaled(5);
        Glyph = Scaled(21);
        GaugeLabel = Scaled(19);
        GaugeLabelGap = Scaled(8);
        BaseSpacing = Scaled(24);
        Item = Gauge + GaugeLabelGap + GaugeLabel;

        CardWidth = Scaled(248);
        CardCorner = Scaled(22);
        CardPaddingH = Scaled(13);
        CardPaddingV = Scaled(14);

        TailWidth = Scaled(26);
        TailHeight = Scaled(30);
        TailGap = Scaled(12);

        SupportGap = Scaled(11);
        SupportButton = Scaled(22);
        SupportBlock = SupportGap + Hairline + SupportButton;

        MiniWidth = Scaled(8);
        MiniHeight = Scaled(52);
        MiniTarget = Math.Max(24, Scaled(24));

        BarHeight = Scaled(5);
        RowLine = Scaled(16);
        RowMeta = Scaled(14);
        ShadowSlack = Scaled(22);

        CardTrailingInset = RailWidth + TailGap;
        PanelWidth = CardTrailingInset + CardWidth + TailWidth + ShadowSlack;

        Card = new CardMetrics(this);
    }

    public static Metrics For(OverlaySize size) => size switch
    {
        OverlaySize.Small => SmallSize,
        OverlaySize.Large => LargeSize,
        _ => MediumSize
    };

    public double Scale { get; }

    public double RailWidth { get; }
    public double RailCorner { get; }
    public double RailPadding { get; }

    public double Gauge { get; }
    public double GaugeRing { get; }
    public double Glyph { get; }
    public double GaugeLabel { get; }
    public double GaugeLabelGap { get; }
    public double BaseSpacing { get; }

    /// <summary>Gauge, gap and caption together: one row of the rail.</summary>
    public double Item { get; }

    public double CardWidth { get; }
    public double CardCorner { get; }
    public double CardPaddingH { get; }
    public double CardPaddingV { get; }

    public double TailWidth { get; }
    public double TailHeight { get; }
    public double TailGap { get; }

    public double SupportGap { get; }
    public double SupportButton { get; }
    public double SupportBlock { get; }

    public double MiniWidth { get; }
    public double MiniHeight { get; }
    public double MiniTarget { get; }

    public double BarHeight { get; }
    public double RowLine { get; }
    public double RowMeta { get; }
    public double ShadowSlack { get; }

    public double CardTrailingInset { get; }
    public double PanelWidth { get; }

    public CardMetrics Card { get; }

    /// <summary>The inset the card's tail may not pass, so it stays clear of the rounded corners.</summary>
    public double TailInset => CardCorner + TailHeight / 2;

    /// <summary>How far apart the gauges sit. A long rail tightens up rather than scrolling early.</summary>
    public double ItemSpacing(int count) => count switch
    {
        <= 4 => BaseSpacing,
        <= 6 => Scaled(16),
        <= 9 => Scaled(9),
        _ => Scaled(6)
    };

    public double RailHeight(int count, double spacing)
    {
        var rows = Math.Max(1, count);
        return rows * Item + (rows - 1) * spacing + RailPadding * 2 + SupportBlock;
    }

    /// <summary>
    /// Scales one metric and rounds it, as macOS does.
    /// </summary>
    /// <remarks>
    /// Away from zero, not to even: Swift's <c>rounded()</c> rounds halves up, while
    /// <see cref="Math.Round(double)"/> defaults to banker's rounding and would round 22.5 down to 22.
    /// </remarks>
    internal double Scaled(double value) => Math.Round(value * Scale, MidpointRounding.AwayFromZero);
}

/// <summary>The detail card's own measurements, mirroring <c>CardMetrics</c> on macOS.</summary>
internal sealed class CardMetrics
{
    public const int MaximumRows = 3;

    private readonly Metrics _metrics;

    internal CardMetrics(Metrics metrics)
    {
        _metrics = metrics;
        Header = metrics.Scaled(22);
        HeaderGap = metrics.Scaled(11);
        Row = metrics.Scaled(46);
        RowSpacing = metrics.Scaled(11);
        State = metrics.Scaled(66);
        FooterLead = metrics.Scaled(10);
        FooterTrail = metrics.Scaled(9);
        Footer = metrics.Scaled(30);
    }

    public double Header { get; }
    public double HeaderGap { get; }
    public double Row { get; }
    public double RowSpacing { get; }
    public double State { get; }
    public double FooterLead { get; }
    public double FooterTrail { get; }
    public double Footer { get; }

    public int RowCount(ProviderSnapshot snapshot)
        => Math.Min(MaximumRows, Math.Max(1, snapshot.FeaturedWindows(MaximumRows).Count));

    public double Height(ProviderSnapshot snapshot)
    {
        double body;
        if (snapshot.Status == ProviderStatus.Ready && snapshot.Windows.Count > 0)
        {
            var rows = RowCount(snapshot);
            body = rows * Row + (rows - 1) * RowSpacing;
        }
        else
        {
            body = State;
        }

        // The bare 1 is the footer divider, a hairline that does not scale.
        return _metrics.CardPaddingV * 2 + Header + HeaderGap + body
             + FooterLead + Metrics.Hairline + FooterTrail + Footer;
    }
}

/// <summary>Rounds a length so it lands on whole device pixels.</summary>
internal static class DevicePixels
{
    /// <summary>
    /// The nearest whole number of device pixels to <paramref name="dips"/>, never fewer than one,
    /// converted back to DIPs.
    /// </summary>
    /// <remarks>
    /// A literal 1 DIP hairline covers 1.5 device pixels at 150% and renders as a grey smear. macOS
    /// has no equivalent problem because its scale factors are integers.
    /// </remarks>
    public static double Snap(double dips, double dpiScale)
    {
        if (dpiScale <= 0 || double.IsNaN(dpiScale))
            throw new ArgumentOutOfRangeException(nameof(dpiScale), dpiScale, "DPI scale must be positive.");

        var pixels = Math.Max(1, Math.Round(dips * dpiScale, MidpointRounding.AwayFromZero));
        return pixels / dpiScale;
    }
}
