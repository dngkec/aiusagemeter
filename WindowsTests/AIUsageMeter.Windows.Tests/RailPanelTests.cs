using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;
using AIUsageMeter.Windows.Overlay;

namespace AIUsageMeter.Windows.Tests;

[TestClass]
public sealed class RailPanelTests
{
    private static readonly Metrics Sizes = Metrics.For(OverlaySize.Medium);
    private static readonly Typo Ramp = Typo.For(OverlaySize.Medium);

    private static ProviderSnapshot Snapshot(ProviderId id, double percent = 40)
        => new(id, [new UsageWindow("w", "Window", percent, 100)]);

    private static T OnRail<T>(IReadOnlyList<ProviderSnapshot> snapshots, Func<RailPanel, T> read)
        => Rendering.Sta(() =>
        {
            var rail = new RailPanel(Sizes, Ramp);
            rail.SetProviders(snapshots);
            rail.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            rail.Arrange(new Rect(0, 0, rail.DesiredSize.Width, rail.DesiredSize.Height));
            rail.UpdateLayout();
            return read(rail);
        });

    [TestMethod]
    public void TheRailIsAsWideAsTheMetricsSay()
        => Assert.AreEqual(Sizes.RailWidth, OnRail([Snapshot(ProviderId.Claude)], x => x.DesiredSize.Width));

    [TestMethod]
    public void TheRailIsTallEnoughForItsGaugesThePaddingAndTheHeart()
    {
        var three = new[] { Snapshot(ProviderId.Claude), Snapshot(ProviderId.Codex), Snapshot(ProviderId.Grok) };
        Assert.AreEqual(Sizes.RailHeight(3, 24), OnRail(three, x => x.DesiredSize.Height));
        Assert.AreEqual(337d, OnRail(three, x => x.DesiredSize.Height));
    }

    [TestMethod]
    public void TheGaugesTightenUpAsProvidersAreAdded()
    {
        var five = ProviderInfo.All.Take(5).Select(x => Snapshot(x)).ToList();
        Assert.AreEqual(16d, OnRail(five, x => x.Spacing));
        Assert.AreEqual(24d, OnRail([Snapshot(ProviderId.Claude)], x => x.Spacing));
    }

    [TestMethod]
    public void EachGaugeCentreIsWhereTheCardShouldPointAtIt()
    {
        var three = new[] { Snapshot(ProviderId.Claude), Snapshot(ProviderId.Codex), Snapshot(ProviderId.Grok) };

        // railPadding 18 + index * (item 73 + spacing 24) + half a gauge.
        Assert.AreEqual(18 + 23d, OnRail(three, x => x.GaugeCentre(ProviderId.Claude)));
        Assert.AreEqual(18 + 97 + 23d, OnRail(three, x => x.GaugeCentre(ProviderId.Codex)));
        Assert.AreEqual(18 + 194 + 23d, OnRail(three, x => x.GaugeCentre(ProviderId.Grok)));
    }

    [TestMethod]
    public void AnEmptyRailOffersToSetUpAProvider()
    {
        var kinds = OnRail([], x => x.Children.OfType<SetupButton>().Count());
        Assert.AreEqual(1, kinds);
        Assert.AreEqual(0, OnRail([Snapshot(ProviderId.Claude)], x => x.Children.OfType<SetupButton>().Count()));
    }

    [TestMethod]
    public void RefreshingAProviderReusesItsRowRatherThanRebuildingIt()
    {
        // Rebuilding drops the pointer out of whichever gauge it was over, closing the card mid-hover.
        var same = Rendering.Sta(() =>
        {
            var rail = new RailPanel(Sizes, Ramp);
            rail.SetProviders([Snapshot(ProviderId.Claude, 10)]);
            var first = rail.Gauges[0];
            rail.SetProviders([Snapshot(ProviderId.Claude, 90)]);
            return ReferenceEquals(first, rail.Gauges[0]);
        });

        Assert.IsTrue(same);
    }

    [TestMethod]
    public void ChangingWhichProvidersAreShownReplacesTheRows()
    {
        var ids = Rendering.Sta(() =>
        {
            var rail = new RailPanel(Sizes, Ramp);
            rail.SetProviders([Snapshot(ProviderId.Claude), Snapshot(ProviderId.Codex)]);
            rail.SetProviders([Snapshot(ProviderId.Grok)]);
            return rail.Gauges.Select(x => x.Id).ToArray();
        });

        CollectionAssert.AreEqual(new[] { ProviderId.Grok }, ids);
    }

    [TestMethod]
    public void OnlyTheGaugeWithAnOpenCardIsActive()
    {
        var active = Rendering.Sta(() =>
        {
            var rail = new RailPanel(Sizes, Ramp);
            rail.SetProviders([Snapshot(ProviderId.Claude), Snapshot(ProviderId.Codex)]);
            rail.SetActive(ProviderId.Codex);
            return rail.Gauges.Select(x => x.Active).ToArray();
        });

        CollectionAssert.AreEqual(new[] { false, true }, active);
    }

    [TestMethod]
    public void TheRailIsBlackWithItsLeadingCornersCutAway()
    {
        var probe = Rendering.Sta(() =>
        {
            var rail = new RailPanel(Sizes, Ramp);
            rail.SetProviders([Snapshot(ProviderId.Claude), Snapshot(ProviderId.Codex), Snapshot(ProviderId.Grok)]);
            rail.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Rendering.Render(rail, Sizes.RailWidth, rail.DesiredSize.Height, blackGround: false);
        });

        Assert.AreEqual(255, probe.AlphaAt(Sizes.RailWidth - 2, 2), "the trailing corner stays square");
        Assert.AreEqual(255, probe.AlphaAt(Sizes.RailWidth / 2, 200), "the rail body is solid");

        // Not quite nought: the drop shadow reaches past the rail, which is the point of it.
        Assert.IsLessThan(16, probe.AlphaAt(1, 1), "the leading corner is rounded away");
    }
}
