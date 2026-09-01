using AIUsageMeter.Core;

namespace AIUsageMeter.Core.Tests;

/// <summary>
/// Geometry and hover rules ported from the macOS build. Expected values are derived from
/// <c>Sources/AIUsageMeterCore/Layout.swift</c> and <c>Sources/AIUsageMeterCore/Hover.swift</c>,
/// so a failure here means the two apps have drifted apart.
/// </summary>
[TestClass]
public sealed class OverlayGeometryTests
{
    // Metrics.cardCorner + Metrics.tailHeight / 2, the inset the tail may not pass.
    private const double TailInset = 37;
    private static readonly MeterRect Screen = new(0, 0, 1920, 1000);

    // MARK: Place

    [TestMethod]
    public void PlacePinsTopAndBottomFortyPointsFromTheWorkAreaEdge()
    {
        Assert.AreEqual(40d, OverlayLayout.Place(Screen, 380, 200, VerticalPosition.Top, 0).Y);
        Assert.AreEqual(760d, OverlayLayout.Place(Screen, 380, 200, VerticalPosition.Bottom, 0).Y);
    }

    [TestMethod]
    public void PlaceGivesTheOverlayAFloorHeight()
        => Assert.AreEqual(116d, OverlayLayout.Place(Screen, 380, 50, VerticalPosition.Center, 0).Height);

    // MARK: PanelFrame

    [TestMethod]
    public void PanelFrameGrowsAroundTheRailMidpointSoTheRailDoesNotMove()
    {
        var rail = OverlayLayout.Place(Screen, 380, 400, VerticalPosition.Center, 0);
        var panel = OverlayLayout.PanelFrame(Screen, 380, 400, 600, VerticalPosition.Center, 0);

        Assert.AreEqual(600d, panel.Height);
        Assert.AreEqual(rail.MidY, panel.MidY);
        Assert.AreEqual(200d, panel.Y);
    }

    [TestMethod]
    public void PanelFrameNeverShrinksBelowTheRail()
        => Assert.AreEqual(400d, OverlayLayout.PanelFrame(Screen, 380, 400, 200, VerticalPosition.Center, 0).Height);

    [TestMethod]
    public void PanelFrameStaysInsideTheWorkArea()
    {
        var panel = OverlayLayout.PanelFrame(Screen, 380, 400, 600, VerticalPosition.Top, 0);
        Assert.AreEqual(0d, panel.Y);
        Assert.IsTrue(panel.MaxY <= Screen.MaxY);
    }

    [TestMethod]
    public void PanelFrameHugsTheRightEdge()
        => Assert.AreEqual(1540d, OverlayLayout.PanelFrame(Screen, 380, 400, 600, VerticalPosition.Center, 0).X);

    // MARK: MiniFrame

    [TestMethod]
    public void MiniFrameCentresTheTabOnWhereTheRailWouldHaveBeen()
    {
        var rail = OverlayLayout.Place(Screen, 72, 400, VerticalPosition.Center, 0);
        var tab = OverlayLayout.MiniFrame(Screen, 24, 78, 400, VerticalPosition.Center, 0);

        Assert.AreEqual(rail.MidY, tab.MidY);
        Assert.AreEqual(461d, tab.Y);
        Assert.AreEqual(1896d, tab.X);
        Assert.AreEqual(78d, tab.Height);
    }

    // MARK: CardPlacement

    [TestMethod]
    public void CardPlacementCentresOnItsGaugeWhenThereIsRoom()
    {
        var (centre, tail) = OverlayLayout.CardPlacement(0, 200, 800, TailInset);
        Assert.AreEqual(0d, centre);
        Assert.AreEqual(100d, tail);
    }

    [TestMethod]
    public void CardPlacementClampsToThePanelAndAimsTheTailBackUpAtItsGauge()
    {
        var (centre, tail) = OverlayLayout.CardPlacement(-350, 200, 800, TailInset);
        Assert.AreEqual(-294d, centre);
        Assert.AreEqual(44d, tail);
    }

    [TestMethod]
    public void CardPlacementClampsToThePanelAndAimsTheTailBackDownAtItsGauge()
    {
        var (centre, tail) = OverlayLayout.CardPlacement(350, 200, 800, TailInset);
        Assert.AreEqual(294d, centre);
        Assert.AreEqual(156d, tail);
    }

    [TestMethod]
    public void CardPlacementKeepsTheTailClearOfTheRoundedCorners()
    {
        Assert.AreEqual(TailInset, OverlayLayout.CardPlacement(-1000, 200, 800, TailInset).TailCentre);
        Assert.AreEqual(200 - TailInset, OverlayLayout.CardPlacement(1000, 200, 800, TailInset).TailCentre);
    }

    [TestMethod]
    public void CardPlacementCentresACardTallerThanThePanel()
    {
        var (centre, tail) = OverlayLayout.CardPlacement(100, 900, 800, TailInset);
        Assert.AreEqual(0d, centre);
        Assert.AreEqual(550d, tail);
    }

    [TestMethod]
    public void CardPlacementCollapsesTheTailRangeOnACardShorterThanTwoInsets()
    {
        // Both bounds land on the same point rather than crossing over.
        var (_, tail) = OverlayLayout.CardPlacement(0, 40, 800, TailInset);
        Assert.AreEqual(20d, tail);
    }

    // MARK: OverlaySize

    [TestMethod]
    public void OverlaySizeScaleMatchesTheMacBuild()
    {
        Assert.AreEqual(0.86, OverlaySize.Small.Scale(), 1e-9);
        Assert.AreEqual(1.00, OverlaySize.Medium.Scale(), 1e-9);
        Assert.AreEqual(1.18, OverlaySize.Large.Scale(), 1e-9);
    }

    [TestMethod]
    public void PreferencesStillLoadTheRetiredCompactOverlaySizeName()
    {
        var path = TemporaryPreferences("""
            {"schemaVersion":2,"providers":[],"overlaySize":"Compact"}
            """);
        try
        {
            Assert.AreEqual(OverlaySize.Small, new PreferencesStore(path).Load().OverlaySize);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task OverlaySizeRoundTripsThroughASavedFile()
    {
        var path = TemporaryPreferences("{}");
        try
        {
            var store = new PreferencesStore(path);
            await store.SaveAsync(AppPreferences.Defaults with { OverlaySize = OverlaySize.Large });
            Assert.AreEqual(OverlaySize.Large, store.Load().OverlaySize);
        }
        finally { File.Delete(path); }
    }

    // MARK: UsageColor

    [TestMethod]
    public void UsageColourHexesMatchTheMacBuild()
    {
        // Sources/AIUsageMeter/Design.swift, Color.usage(percent:status:).
        Assert.AreEqual("#14FF97", UsageColor.For(0));
        Assert.AreEqual("#EDFF05", UsageColor.For(50));
        Assert.AreEqual("#FF9F0A", UsageColor.For(70));
        Assert.AreEqual("#FF453A", UsageColor.For(90));
    }

    [TestMethod]
    public void NoTwoProvidersShareAMonogramOnTheRail()
    {
        // The monogram is all the reader gets for these, so two the same is two unreadable gauges.
        // Sources/AIUsageMeter/Glyphs.swift letters Kimi K, Zai Z, JetBrains JB and Kilo Ki.
        var lettered = new[] { ProviderId.Kimi, ProviderId.Zai, ProviderId.JetBrainsAI, ProviderId.Kilo };
        var monograms = lettered.Select(x => x.Monogram()).ToArray();

        CollectionAssert.AreEqual(new[] { "K", "Z", "JB", "Ki" }, monograms);
        Assert.HasCount(lettered.Length, monograms.Distinct().ToArray());
    }

    private static string TemporaryPreferences(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aiusagemeter-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}

/// <summary>
/// Hover rules ported from <c>Sources/AIUsageMeterCore/Hover.swift</c>. Enter and exit arrive in
/// either order and one can go missing, so these cover the disorder, not just the happy path.
/// </summary>
[TestClass]
public sealed class HoverTrackerTests
{
    [TestMethod]
    public void AFreshTrackerDismisses()
    {
        var tracker = new HoverTracker();
        Assert.IsNull(tracker.Target);
        Assert.AreEqual(HoverDecision.Dismiss, tracker.Decision);
        Assert.IsFalse(tracker.HoldsOpen);
    }

    [TestMethod]
    public void EnteringAGaugeOpensIt()
    {
        var tracker = new HoverTracker();
        tracker.Gauge(ProviderId.Claude, true);

        Assert.AreEqual(ProviderId.Claude, tracker.Target);
        Assert.AreEqual(HoverDecision.Open, tracker.Decision);
        Assert.IsTrue(tracker.HoldsOpen);
    }

    [TestMethod]
    public void TheLastGaugeEnteredWins()
    {
        var tracker = new HoverTracker();
        tracker.Gauge(ProviderId.Claude, true);
        tracker.Gauge(ProviderId.Codex, true);

        Assert.AreEqual(ProviderId.Codex, tracker.Target);
    }

    [TestMethod]
    public void LeavingAGaugeFallsBackToTheOneStillUnderThePointer()
    {
        // The exit for Claude never arrived; Codex's exit must not dismiss the overlay.
        var tracker = new HoverTracker();
        tracker.Gauge(ProviderId.Claude, true);
        tracker.Gauge(ProviderId.Codex, true);
        tracker.Gauge(ProviderId.Codex, false);

        Assert.AreEqual(ProviderId.Claude, tracker.Target);
    }

    [TestMethod]
    public void LeavingAGaugeNeverEnteredChangesNothing()
    {
        var tracker = new HoverTracker();
        tracker.Gauge(ProviderId.Claude, true);
        tracker.Gauge(ProviderId.Codex, false);

        Assert.AreEqual(ProviderId.Claude, tracker.Target);
    }

    [TestMethod]
    public void EnteringAGaugeImpliesTheRailSoLeavingItOnlyKeeps()
    {
        var tracker = new HoverTracker();
        tracker.Gauge(ProviderId.Claude, true);
        tracker.Gauge(ProviderId.Claude, false);

        Assert.IsNull(tracker.Target);
        Assert.AreEqual(HoverDecision.Keep, tracker.Decision);
    }

    [TestMethod]
    public void LeavingTheRailForgetsEveryGauge()
    {
        var tracker = new HoverTracker();
        tracker.Gauge(ProviderId.Claude, true);
        tracker.Rail(false);

        Assert.IsNull(tracker.Target);
        Assert.AreEqual(HoverDecision.Dismiss, tracker.Decision);
    }

    [TestMethod]
    public void TheCardHoldsTheOverlayOpenWhileThePointerCrossesTheGap()
    {
        var tracker = new HoverTracker();
        tracker.Gauge(ProviderId.Claude, true);
        tracker.Card(true);
        tracker.Rail(false);

        Assert.AreEqual(HoverDecision.Keep, tracker.Decision);
        Assert.IsTrue(tracker.HoldsOpen);
    }

    [TestMethod]
    public void ResetForgetsEverything()
    {
        var tracker = new HoverTracker();
        tracker.Gauge(ProviderId.Claude, true);
        tracker.Card(true);
        tracker.Reset();

        Assert.AreEqual(HoverDecision.Dismiss, tracker.Decision);
    }
}
