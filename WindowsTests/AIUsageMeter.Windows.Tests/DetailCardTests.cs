using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;
using AIUsageMeter.Windows.Overlay;

namespace AIUsageMeter.Windows.Tests;

[TestClass]
public sealed class DetailCardTests
{
    private static readonly Metrics Sizes = Metrics.For(OverlaySize.Medium);
    private static readonly Typo Ramp = Typo.For(OverlaySize.Medium);
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private static ProviderSnapshot Ready(int windows = 2, DataSourceKind source = DataSourceKind.Live)
        => new(ProviderId.Claude,
            Enumerable.Range(0, windows)
                .Select(i => new UsageWindow($"w{i}", $"Window {i}", 30 + i * 20, 100, Now.AddHours(4)))
                .ToList(),
            ProviderStatus.Ready, source, UpdatedAt: Now.AddSeconds(-10));

    private static ProviderSnapshot Stalled(ProviderStatus status = ProviderStatus.SetupNeeded)
        => new(ProviderId.Claude, [], status, Message: "Sign in to Claude Code first.", UpdatedAt: Now);

    private static T Build<T>(ProviderSnapshot snapshot, Func<DetailCard, T> read)
        => Rendering.Sta(() =>
        {
            var card = new DetailCard(snapshot, Sizes, Ramp, Now) { TailCentre = 60 };
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            card.Arrange(new Rect(0, 0, card.DesiredSize.Width, card.DesiredSize.Height));
            card.UpdateLayout();
            return read(card);
        });

    [TestMethod]
    public void TheCardIsAsWideAsItsBodyAndTailTogether()
        => Assert.AreEqual(274d, Build(Ready(), x => x.DesiredSize.Width));

    [TestMethod]
    public void TheCardsHeightComesFromTheSharedMetrics()
    {
        // So the layout can place the card before it has been built.
        Assert.AreEqual(Sizes.Card.Height(Ready(1)), Build(Ready(1), x => x.DesiredSize.Height));
        Assert.AreEqual(Sizes.Card.Height(Ready(3)), Build(Ready(3), x => x.DesiredSize.Height));
        Assert.AreEqual(271d, Build(Ready(3), x => x.DesiredSize.Height));
    }

    [TestMethod]
    public void AReadyProviderGetsARowPerUsageWindow()
    {
        Assert.AreEqual(2, Build(Ready(2), x => x.Children.OfType<UsageRowControl>().Count()));
        Assert.AreEqual(0, Build(Ready(2), x => x.Children.OfType<CardStateBlock>().Count()));
    }

    [TestMethod]
    public void NoMoreThanThreeRowsAreEverShown()
        => Assert.AreEqual(3, Build(Ready(7), x => x.Children.OfType<UsageRowControl>().Count()));

    [TestMethod]
    public void AProviderWithNothingToShowGetsTheStateBlockInstead()
    {
        Assert.AreEqual(1, Build(Stalled(), x => x.Children.OfType<CardStateBlock>().Count()));
        Assert.AreEqual(0, Build(Stalled(), x => x.Children.OfType<UsageRowControl>().Count()));
        Assert.AreEqual(177d, Build(Stalled(), x => x.DesiredSize.Height));
    }

    [TestMethod]
    public void AProviderNeedingAttentionOffersSettingsAndOneWithADashboardOffersThat()
    {
        Assert.AreEqual(1, Build(Stalled(), x => x.Children.OfType<CardAction>().Count()));

        var withDashboard = Ready() with { DashboardUrl = new Uri("https://example.invalid/usage") };
        Assert.AreEqual(1, Build(withDashboard, x => x.Children.OfType<CardAction>().Count()));

        // A healthy provider with nowhere to send you gets no action at all.
        Assert.AreEqual(0, Build(Ready(), x => x.Children.OfType<CardAction>().Count()));
    }

    [TestMethod]
    public void DemoDataSaysSoInTheFooter()
    {
        var texts = Build(Ready(source: DataSourceKind.Demo),
            x => x.Children.OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToArray());

        Assert.Contains("Demo data", texts);
        Assert.Contains("Deterministic sample data", texts);
    }

    [TestMethod]
    public void TheCardIsBlackWithARoundedLeadingEdgeAndATailBesideItsGauge()
    {
        var probe = Rendering.Sta(() =>
        {
            var card = new DetailCard(Ready(3), Sizes, Ramp, Now) { TailCentre = 100 };
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Rendering.Render(card, card.DesiredSize.Width, card.DesiredSize.Height, blackGround: false);
        });

        Assert.AreEqual(255, probe.AlphaAt(120, 100), "the body is solid");
        Assert.IsLessThan(40, probe.AlphaAt(1, 1), "the leading corner is rounded away");
        Assert.AreEqual(255, probe.AlphaAt(268, 100), "the tail reaches out beside its gauge");
        Assert.IsLessThan(200, probe.AlphaAt(268, 20), "and nowhere else down that edge");
    }

    [TestMethod]
    public void TheTailFollowsWhicheverGaugeTheCardBelongsTo()
    {
        var high = Rendering.Sta(() =>
        {
            var card = new DetailCard(Ready(3), Sizes, Ramp, Now) { TailCentre = 60 };
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Rendering.Render(card, card.DesiredSize.Width, card.DesiredSize.Height, blackGround: false);
        });

        Assert.AreEqual(255, high.AlphaAt(268, 60));
        Assert.IsLessThan(200, high.AlphaAt(268, 200));
    }
}

[TestClass]
public sealed class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RecentReadingsSayJustNow()
    {
        Assert.AreEqual("just now", RelativeTime.Short(Now, Now));
        Assert.AreEqual("just now", RelativeTime.Short(Now.AddSeconds(-44), Now));
    }

    [TestMethod]
    public void OlderReadingsCountUpInTheLargestUnitThatFits()
    {
        Assert.AreEqual("1 min ago", RelativeTime.Short(Now.AddSeconds(-60), Now));
        Assert.AreEqual("59 min ago", RelativeTime.Short(Now.AddMinutes(-59), Now));
        Assert.AreEqual("2 h ago", RelativeTime.Short(Now.AddHours(-2), Now));
        Assert.AreEqual("3 d ago", RelativeTime.Short(Now.AddDays(-3), Now));
    }

    [TestMethod]
    public void AWindowAlreadyPastItsResetSaysSo()
        => Assert.AreEqual("Resetting…", RelativeTime.Reset(Now.AddMinutes(-1), Now));

    [TestMethod]
    public void ResetsWithinTheHourCountDownInMinutes()
    {
        Assert.AreEqual("Resets in 30 min", RelativeTime.Reset(Now.AddMinutes(30), Now));
        // Never "in 0 min": under a minute still reads as one.
        Assert.AreEqual("Resets in 1 min", RelativeTime.Reset(Now.AddSeconds(20), Now));
    }

    [TestMethod]
    public void EveryResetLineUsesTheSameClockFormat()
    {
        // A card can carry both of these at once, and a 24-hour line under a 12-hour one reads as a bug.
        var soon = RelativeTime.Reset(Now.AddHours(6), Now);
        var later = RelativeTime.Reset(Now.AddHours(30), Now);

        var soonTime = soon["Resets ".Length..];
        Assert.EndsWith(soonTime, later, $"'{later}' should end with the same clock as '{soon}'");
    }

    [TestMethod]
    public void AResetMoreThanHalfADayOutNamesTheDay()
        => Assert.Contains(Now.AddHours(30).ToLocalTime().ToString("ddd"), RelativeTime.Reset(Now.AddHours(30), Now));

    [TestMethod]
    public void AWindowWithNoResetSaysThatRatherThanGuessing()
        => Assert.AreEqual("No reset scheduled", RelativeTime.ResetLine(new UsageWindow("w", "W", 1, 2), Now));
}

[TestClass]
public sealed class CardHeaderTests
{
    private static readonly Metrics Sizes = Metrics.For(OverlaySize.Medium);
    private static readonly Typo Ramp = Typo.For(OverlaySize.Medium);
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private static ProviderSnapshot Any =>
        new(ProviderId.Claude, [new UsageWindow("w", "Window", 30, 100)], UpdatedAt: Now);

    [TestMethod]
    public void APinnedCardShowsThePinAndAnUnpinnedOneDoesNot()
    {
        var (unpinned, pinned) = Rendering.Sta(() =>
        {
            var card = new DetailCard(Any, Sizes, Ramp, Now);
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var before = card.Children.OfType<PinMark>().Single().Visibility;
            card.Pinned = true;
            return (before, card.Children.OfType<PinMark>().Single().Visibility);
        });

        Assert.AreEqual(System.Windows.Visibility.Collapsed, unpinned);
        Assert.AreEqual(System.Windows.Visibility.Visible, pinned);
    }
}
