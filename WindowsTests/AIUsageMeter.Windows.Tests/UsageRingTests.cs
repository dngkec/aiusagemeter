using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;
using AIUsageMeter.Windows.Overlay;

namespace AIUsageMeter.Windows.Tests;

/// <summary>
/// The gauge, checked by rasterising it and looking at the pixels. Geometry tests prove the arcs are
/// right; these prove the control paints them where and in what colour macOS does.
/// </summary>
[TestClass]
public sealed class UsageRingTests
{
    private static readonly Metrics Medium = Metrics.For(OverlaySize.Medium);
    private static Color Track => Blend(Palette.RingTrack.Color, Palette.RingTrack.Opacity);
    private static Color Green => Palette.Usage(10).Color;
    private static Color Red => Palette.Usage(95).Color;

    /// <summary>The palette's translucent whites over the overlay's black surface.</summary>
    private static Color Blend(Color over, double opacity)
        => Color.FromRgb((byte)Math.Round(over.R * opacity), (byte)Math.Round(over.G * opacity), (byte)Math.Round(over.B * opacity));

    private static Rendering.Probe Draw(double percent, bool refreshing = false, ProviderGlyph? glyph = null)
        => Rendering.Sta(() =>
        {
            var ring = new UsageRing
            {
                Metrics = Medium,
                Glyph = glyph ?? ProviderGlyphs.Burst,
                Percent = percent,
                Tint = Palette.Usage(percent),
                Refreshing = refreshing
            };
            return Rendering.Render(ring, Medium.Gauge, Medium.Gauge);
        });

    /// <summary>Radius of the ring's path, where the arc's colour should be found.</summary>
    private static double PathRadius => UsageRing.RadiusFor(Medium.Gauge, Medium.GaugeRing);

    [TestMethod]
    public void TheRingPathSitsHalfAStrokeInsideItsBox()
    {
        // 46 wide with a 5 stroke: the path runs at 20.5, so the stroke's outer edge lands on 23.
        Assert.AreEqual(20.5, UsageRing.RadiusFor(46, 5), 1e-9);
        Assert.AreEqual(23d, UsageRing.RadiusFor(46, 5) + 5 / 2d, 1e-9);
    }

    [TestMethod]
    public void TheGaugeAsksForTheSizeTheRailGivesIt()
    {
        var size = Rendering.Sta(() =>
        {
            var ring = new UsageRing { Metrics = Medium, Glyph = ProviderGlyphs.Burst };
            ring.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            return ring.DesiredSize;
        });

        Assert.AreEqual(Medium.Gauge, size.Width);
        Assert.AreEqual(Medium.Gauge, size.Height);
    }

    [TestMethod]
    public void AnEmptyGaugeIsAllTrackAndNoReading()
    {
        var probe = Draw(0);

        // Twelve o'clock, where a reading would start.
        Assert.IsTrue(Rendering.Probe.Close(probe.AtPolar(PathRadius, -90), Track),
            $"expected track at the top, found {probe.AtPolar(PathRadius, -90)}");
    }

    [TestMethod]
    public void AQuarterFullPaintsTheLeadingQuadrantAndLeavesTheRest()
    {
        var probe = Draw(25);

        // Clockwise from twelve, a quarter reaches three o'clock.
        Assert.IsTrue(Rendering.Probe.Close(probe.AtPolar(PathRadius, -45), Green),
            $"expected the reading at half past one, found {probe.AtPolar(PathRadius, -45)}");
        Assert.IsTrue(Rendering.Probe.Close(probe.AtPolar(PathRadius, 135), Track),
            $"expected track at half past seven, found {probe.AtPolar(PathRadius, 135)}");
    }

    [TestMethod]
    public void AFullGaugeCoversTheWholeRing()
    {
        var probe = Draw(100);

        foreach (var angle in new[] { -90d, -45, 0, 45, 90, 135, 180, 225 })
            Assert.IsTrue(Rendering.Probe.Close(probe.AtPolar(PathRadius, angle), Red),
                $"expected the reading at {angle} degrees, found {probe.AtPolar(PathRadius, angle)}");
    }

    [TestMethod]
    public void TheReadingRunsClockwiseFromTwelveOClock()
    {
        // Ten percent is 36 degrees, so five past twelve is painted and five to twelve is not.
        var probe = Draw(10);

        Assert.IsTrue(Rendering.Probe.Close(probe.AtPolar(PathRadius, -75), Green),
            "just clockwise of twelve should carry the reading");
        Assert.IsTrue(Rendering.Probe.Close(probe.AtPolar(PathRadius, -105), Track),
            "just anticlockwise of twelve should still be track");
    }

    [TestMethod]
    public void TheThresholdColoursReachTheGauge()
    {
        // Sampled where the smallest of these readings still reaches: ten percent is only 36 degrees.
        foreach (var percent in new[] { 10d, 55, 75, 95 })
            Assert.IsTrue(Rendering.Probe.Close(Draw(percent).AtPolar(PathRadius, -75), Palette.Usage(percent).Color),
                $"{percent}% should be painted in {Palette.Usage(percent).Color}, found {Draw(percent).AtPolar(PathRadius, -75)}");
    }

    [TestMethod]
    public void TheProvidersMarkIsDrawnInTheMiddle()
    {
        // An empty mark is the control: whatever ink lands in the middle came from the glyph.
        var blank = Draw(0, glyph: new ProviderGlyph.Vector([])).AtPolar(0, 0);

        Assert.AreEqual(0, Luminance(blank), 1, "nothing should be drawn in the middle without a mark");
        Assert.IsGreaterThan(0d, Luminance(Draw(0).AtPolar(0, 0)), "the burst's core sits dead centre");
        Assert.IsGreaterThan(0d, Luminance(Draw(0, glyph: new ProviderGlyph.Letters("K")).AtPolar(0, 0)),
            "so does a monogram");
    }

    [TestMethod]
    public void RefreshingDimsTheReadingSoTheSweepReadsAgainstIt()
    {
        // Sampled at four o'clock: at fifty percent the reading reaches it, and the sweep — which
        // covers twelve to three when it is not turning — does not, so this sees the fade alone.
        var still = Draw(50).AtPolar(PathRadius, 45);
        var busy = Draw(50, refreshing: true).AtPolar(PathRadius, 45);

        Assert.IsLessThan(Luminance(still), Luminance(busy), "the reading should fade while a refresh runs");
    }

    [TestMethod]
    public void TheSweepIsPaintedOverTheReadingWhileARefreshRuns()
    {
        // The sweep starts at twelve and covers a quarter turn, in the primary white.
        var busy = Draw(50, refreshing: true);

        Assert.IsTrue(Rendering.Probe.Close(busy.AtPolar(PathRadius, -45), Palette.Primary.Color),
            $"expected the sweep at half past one, found {busy.AtPolar(PathRadius, -45)}");
    }

    private static double Luminance(Color colour) => 0.2126 * colour.R + 0.7152 * colour.G + 0.0722 * colour.B;
}
