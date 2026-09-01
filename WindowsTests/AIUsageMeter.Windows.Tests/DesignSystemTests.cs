using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Tests;

/// <summary>
/// The design system is a port of <c>Sources/AIUsageMeter/Design.swift</c>. Expected values come
/// from that file, so a failure here means the Windows overlay has drifted from the macOS one.
/// </summary>
[TestClass]
public sealed class MetricsTests
{
    [TestMethod]
    public void MediumIsTheUnscaledMacBuild()
    {
        var m = Metrics.For(OverlaySize.Medium);

        Assert.AreEqual(72d, m.RailWidth);
        Assert.AreEqual(44d, m.RailCorner);
        Assert.AreEqual(46d, m.Gauge);
        Assert.AreEqual(5d, m.GaugeRing);
        Assert.AreEqual(248d, m.CardWidth);
        Assert.AreEqual(22d, m.CardCorner);
        Assert.AreEqual(26d, m.TailWidth);
        Assert.AreEqual(30d, m.TailHeight);
        Assert.AreEqual(73d, m.Item);
        Assert.AreEqual(380d, m.PanelWidth);
    }

    [TestMethod]
    public void SmallAndLargeScaleEveryMetricAndRoundIt()
    {
        var small = Metrics.For(OverlaySize.Small);
        var large = Metrics.For(OverlaySize.Large);

        Assert.AreEqual(62d, small.RailWidth);   // 72 * 0.86 = 61.92
        Assert.AreEqual(40d, small.Gauge);       // 46 * 0.86 = 39.56
        Assert.AreEqual(213d, small.CardWidth);  // 248 * 0.86 = 213.28
        Assert.AreEqual(326d, small.PanelWidth);

        Assert.AreEqual(85d, large.RailWidth);   // 72 * 1.18 = 84.96
        Assert.AreEqual(54d, large.Gauge);       // 46 * 1.18 = 54.28
        Assert.AreEqual(293d, large.CardWidth);  // 248 * 1.18 = 292.64
        Assert.AreEqual(449d, large.PanelWidth);
    }

    [TestMethod]
    public void DerivedMetricsAgreeWithTheirPartsAtEverySize()
    {
        foreach (var size in Enum.GetValues<OverlaySize>())
        {
            var m = Metrics.For(size);
            Assert.AreEqual(m.Gauge + m.GaugeLabelGap + m.GaugeLabel, m.Item, $"Item at {size}");
            Assert.AreEqual(m.SupportGap + Metrics.Hairline + m.SupportButton, m.SupportBlock, $"SupportBlock at {size}");
            Assert.AreEqual(m.RailWidth + m.TailGap, m.CardTrailingInset, $"CardTrailingInset at {size}");
            Assert.AreEqual(m.CardTrailingInset + m.CardWidth + m.TailWidth + m.ShadowSlack, m.PanelWidth, $"PanelWidth at {size}");
        }
    }

    [TestMethod]
    public void TheHairlineInsideTheSupportBlockNeverScales()
    {
        // 13 + 1 + 26 at Large. A scaled hairline would round the block up to 41.
        Assert.AreEqual(29d, Metrics.For(OverlaySize.Small).SupportBlock);
        Assert.AreEqual(34d, Metrics.For(OverlaySize.Medium).SupportBlock);
        Assert.AreEqual(40d, Metrics.For(OverlaySize.Large).SupportBlock);
    }

    [TestMethod]
    public void ItemSpacingTightensAsProvidersAreAdded()
    {
        var m = Metrics.For(OverlaySize.Medium);

        Assert.AreEqual(24d, m.ItemSpacing(1));
        Assert.AreEqual(24d, m.ItemSpacing(4));
        Assert.AreEqual(16d, m.ItemSpacing(5));
        Assert.AreEqual(16d, m.ItemSpacing(6));
        Assert.AreEqual(9d, m.ItemSpacing(7));
        Assert.AreEqual(9d, m.ItemSpacing(9));
        Assert.AreEqual(6d, m.ItemSpacing(10));
    }

    [TestMethod]
    public void RailHeightAddsThePaddingAndTheSupportBlock()
    {
        // 3 * 73 + 2 * 24 + 2 * 18 + 34
        Assert.AreEqual(337d, Metrics.For(OverlaySize.Medium).RailHeight(3, 24));
    }

    [TestMethod]
    public void MiniTargetNeverDropsBelowTheMinimumPointerTarget()
    {
        Assert.AreEqual(24d, Metrics.For(OverlaySize.Small).MiniTarget);   // 24 * 0.86 rounds to 21
        Assert.AreEqual(28d, Metrics.For(OverlaySize.Large).MiniTarget);
    }
}

[TestClass]
public sealed class CardMetricsTests
{
    private static readonly CardMetrics Card = Metrics.For(OverlaySize.Medium).Card;

    private static ProviderSnapshot Snapshot(int windows, ProviderStatus status = ProviderStatus.Ready)
        => new(ProviderId.Claude,
            Enumerable.Range(0, windows).Select(i => new UsageWindow($"w{i}", $"Window {i}", 10, 100)).ToList(),
            status);

    [TestMethod]
    public void MediumIsTheUnscaledMacBuild()
    {
        Assert.AreEqual(22d, Card.Header);
        Assert.AreEqual(46d, Card.Row);
        Assert.AreEqual(66d, Card.State);
    }

    [TestMethod]
    public void HeightGrowsWithEachUsageRow()
    {
        // 28 padding + 22 header + 11 gap + body + 10 + 1 divider + 9 + 30 footer
        Assert.AreEqual(157d, Card.Height(Snapshot(1)));
        Assert.AreEqual(271d, Card.Height(Snapshot(3)));
    }

    [TestMethod]
    public void HeightUsesTheStateBlockWhenTheProviderIsNotReady()
        => Assert.AreEqual(177d, Card.Height(Snapshot(3, ProviderStatus.SetupNeeded)));

    [TestMethod]
    public void HeightUsesTheStateBlockWhenAReadyProviderReportsNoWindows()
        => Assert.AreEqual(177d, Card.Height(Snapshot(0)));

    [TestMethod]
    public void RowCountClampsToThree()
    {
        Assert.AreEqual(1, Card.RowCount(Snapshot(1)));
        Assert.AreEqual(3, Card.RowCount(Snapshot(3)));
        Assert.AreEqual(3, Card.RowCount(Snapshot(7)));
    }
}

[TestClass]
public sealed class DevicePixelsTests
{
    [TestMethod]
    public void AHairlineIsOneDevicePixelAtEveryScale()
    {
        Assert.AreEqual(1d, DevicePixels.Snap(1, 1.0), 1e-9);
        Assert.AreEqual(0.8, DevicePixels.Snap(1, 1.25), 1e-9);          // 1.25px rounds to 1
        Assert.AreEqual(2d / 1.5, DevicePixels.Snap(1, 1.5), 1e-9);      // 1.5px rounds to 2
        Assert.AreEqual(1d, DevicePixels.Snap(1, 2.0), 1e-9);
    }

    [TestMethod]
    public void ALengthNeverSnapsAwayToNothing()
        => Assert.AreEqual(1d, DevicePixels.Snap(0.2, 1.0), 1e-9);

    [TestMethod]
    public void ThickerLinesSnapToTheirOwnWidth()
        => Assert.AreEqual(2d, DevicePixels.Snap(2, 2.0), 1e-9);

    [TestMethod]
    public void ANonPositiveScaleIsRejected()
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DevicePixels.Snap(1, 0));
}

[TestClass]
public sealed class SpringEaseTests
{
    private static SpringEase Reveal => new(0.30, 0.82);

    [TestMethod]
    public void ASpringStartsAtRest()
    {
        Assert.AreEqual(0d, Reveal.Ease(0), 1e-12);
        Assert.AreEqual(0d, Reveal.Progress(0), 1e-12);
    }

    [TestMethod]
    public void ASpringLandsExactlyOnItsTarget()
        // Not "close to 1": WPF reads the easing at t = 1 to set the final value, so anything else
        // leaves the animated property permanently short of, or past, where it was sent.
        => Assert.AreEqual(1d, Reveal.Ease(1));

    [TestMethod]
    public void TheRawCurveHasAllButSettledByItsSettlingTime()
        => Assert.AreEqual(1d, Reveal.Progress(Reveal.Settling.TotalSeconds), 0.002);

    [TestMethod]
    public void TheRevealSpringSettlesInFourHundredMilliseconds()
        // ln(1000) / (0.82 * 2*pi/0.30)
        => Assert.AreEqual(0.40223, Reveal.Settling.TotalSeconds, 0.0001);

    [TestMethod]
    public void AnUnderdampedSpringOvershootsItsTarget()
    {
        var peak = Enumerable.Range(0, 1001).Max(i => Reveal.Ease(i / 1000d));
        Assert.IsGreaterThan(1.005, peak, "a 0.82 spring should overshoot by about 1%");
    }

    [TestMethod]
    public void SpringsTheSolverCannotHandleAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SpringEase(0.30, 1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SpringEase(0.30, 1.5));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SpringEase(0.30, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SpringEase(0, 0.82));
    }
}

[TestClass]
public sealed class MotionTests
{
    [TestMethod]
    public void TheNamedSpringsMatchTheMacBuild()
    {
        Assert.AreEqual(new SpringEase(0.30, 0.82).Settling, Motion.RevealDuration(false));
        Assert.AreEqual(new SpringEase(0.24, 0.85).Settling, Motion.GeometryDuration(false));
        Assert.AreEqual(new SpringEase(0.45, 0.90).Settling, Motion.ValueDuration(false));
    }

    [TestMethod]
    public void ReducedMotionSwapsInShortEases()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(0.14), Motion.RevealDuration(true));
        Assert.AreEqual(TimeSpan.FromSeconds(0.11), Motion.GeometryDuration(true));
        Assert.AreEqual(TimeSpan.FromSeconds(0.16), Motion.ValueDuration(true));
    }

    [TestMethod]
    public void ReducedMotionNeverOvershoots()
    {
        var eased = Motion.Reveal(true);
        Assert.IsLessThanOrEqualTo(1d, Enumerable.Range(0, 101).Max(i => eased.Ease(i / 100d)));
    }
}

[TestClass]
public sealed class PaletteTests
{
    [TestMethod]
    public void EveryBrushIsFrozenSoItCanBeSharedAndCached()
    {
        foreach (var brush in new[]
                 {
                     Palette.Surface, Palette.Edge, Palette.RingTrack, Palette.BarTrack, Palette.Primary,
                     Palette.Secondary, Palette.Tertiary, Palette.Divider, Palette.Dormant,
                     Palette.ActiveFill, Palette.Heart, Palette.HeartActive, Palette.Sponsor,
                     Palette.GroupFill, Palette.Inset, Palette.ToggleOn, Palette.Warning, Palette.Failure, Palette.FocusRing
                 })
            Assert.IsTrue(brush.IsFrozen);

        Assert.IsTrue(Palette.Usage(42).IsFrozen);
    }

    [TestMethod]
    public void TheSurfaceIsBlackAndTheEdgeIsThirteenPercentWhite()
    {
        Assert.AreEqual(Color.FromRgb(0, 0, 0), Palette.Surface.Color);
        Assert.AreEqual(Colors.White, Palette.Edge.Color);
        Assert.AreEqual(0.13, Palette.Edge.Opacity, 1e-9);
        Assert.AreEqual(0.06, Palette.GroupFill.Opacity, 1e-9);
        Assert.AreEqual(Palette.Usage(0).Color, Palette.ToggleOn.Color);
    }

    [TestMethod]
    public void UsageColoursMatchTheMacBuild()
    {
        Assert.AreEqual(Color.FromRgb(0x14, 0xFF, 0x97), Palette.Usage(0).Color);
        Assert.AreEqual(Color.FromRgb(0xED, 0xFF, 0x05), Palette.Usage(50).Color);
        Assert.AreEqual(Color.FromRgb(0xFF, 0x9F, 0x0A), Palette.Usage(70).Color);
        Assert.AreEqual(Color.FromRgb(0xFF, 0x45, 0x3A), Palette.Usage(90).Color);
    }

    [TestMethod]
    public void UsageColoursChangeAtFiftySeventyAndNinety()
    {
        Assert.AreEqual(Palette.Usage(0).Color, Palette.Usage(49.99).Color);
        Assert.AreEqual(Palette.Usage(50).Color, Palette.Usage(69.99).Color);
        Assert.AreEqual(Palette.Usage(70).Color, Palette.Usage(89.99).Color);
        Assert.AreEqual(Palette.Usage(90).Color, Palette.Usage(1000).Color);
    }

    [TestMethod]
    public void AProviderThatIsNotReadyShowsAsDormant()
    {
        Assert.AreEqual(Palette.Dormant, Palette.Usage(10, ProviderStatus.SetupNeeded));
        Assert.AreEqual(Palette.Dormant, Palette.Usage(10, ProviderStatus.Offline));
        Assert.AreEqual(Palette.Usage(10).Color, Palette.Usage(10, ProviderStatus.Ready).Color);
    }
}
