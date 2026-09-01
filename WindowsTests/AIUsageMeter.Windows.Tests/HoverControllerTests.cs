using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;
using AIUsageMeter.Windows.Overlay;

namespace AIUsageMeter.Windows.Tests;

/// <summary>A scheduler a test can wind forward by hand.</summary>
internal sealed class TestScheduler : IDelayScheduler
{
    private readonly List<Pending> _pending = [];
    private TimeSpan _now;

    public IDisposable After(TimeSpan delay, Action action)
    {
        var item = new Pending(_now + delay, action);
        _pending.Add(item);
        return new Cancellation(() => _pending.Remove(item));
    }

    /// <summary>Moves the clock on and fires whatever has come due, oldest first.</summary>
    public void Advance(TimeSpan span)
    {
        _now += span;
        foreach (var item in _pending.Where(x => x.Due <= _now).OrderBy(x => x.Due).ToList())
        {
            if (!_pending.Remove(item)) continue;
            item.Action();
        }
    }

    public int Outstanding => _pending.Count;

    private sealed record Pending(TimeSpan Due, Action Action);

    private sealed class Cancellation(Action stop) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            stop();
        }
    }
}

/// <summary>
/// The hover rules, ported from <c>AppModel</c>. The delays are the substance: without them the card
/// closes while the pointer is still crossing the gap to reach it.
/// </summary>
[TestClass]
public sealed class HoverControllerTests
{
    private static readonly TimeSpan PastOpen = Motion.OpenDelay + TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan PastDismiss = Motion.DismissDelay + TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan PastIdle = Motion.IdleDelay + TimeSpan.FromSeconds(1);

    private static (HoverController Hover, TestScheduler Clock) Build()
    {
        var clock = new TestScheduler();
        return (new HoverController(clock), clock);
    }

    [TestMethod]
    public void NothingIsOpenToBeginWith()
    {
        var (hover, _) = Build();
        Assert.IsNull(hover.Expanded);
        Assert.IsNull(hover.Pinned);
        Assert.IsFalse(hover.IdleMini);
    }

    [TestMethod]
    public void AGaugeOpensItsCardOnlyAfterTheOpenDelay()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);

        Assert.IsNull(hover.Expanded, "not while the pointer is only passing through");
        clock.Advance(PastOpen);
        Assert.AreEqual(ProviderId.Claude, hover.Expanded);
    }

    [TestMethod]
    public void APointerPassingStraightOverAGaugeNeverOpensIt()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        hover.Gauge(ProviderId.Claude, false);
        hover.Rail(false);
        clock.Advance(PastOpen);

        Assert.IsNull(hover.Expanded);
    }

    [TestMethod]
    public void MovingBetweenGaugesSwapsTheCardWithoutClosingIt()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        clock.Advance(PastOpen);

        hover.Gauge(ProviderId.Codex, true);
        clock.Advance(PastOpen);

        Assert.AreEqual(ProviderId.Codex, hover.Expanded);
    }

    [TestMethod]
    public void TheCardSurvivesThePointerCrossingTheGapToReachIt()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        clock.Advance(PastOpen);

        // Off the gauge, off the rail, and onto the card, all within the dismiss delay.
        hover.Gauge(ProviderId.Claude, false);
        hover.Rail(false);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        hover.Card(true);
        clock.Advance(PastDismiss);

        Assert.AreEqual(ProviderId.Claude, hover.Expanded, "the card should still be open");
    }

    [TestMethod]
    public void LeavingEverythingClosesTheCardOnceTheDismissDelayIsUp()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        clock.Advance(PastOpen);

        hover.Rail(false);
        Assert.AreEqual(ProviderId.Claude, hover.Expanded, "not straight away");

        clock.Advance(PastDismiss);
        Assert.IsNull(hover.Expanded);
    }

    [TestMethod]
    public void ClickingAGaugeKeepsItsCardOpenThroughTheDismissDelay()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        hover.TogglePin(ProviderId.Claude);

        hover.Rail(false);
        clock.Advance(PastDismiss);

        Assert.AreEqual(ProviderId.Claude, hover.Expanded);
        Assert.AreEqual(ProviderId.Claude, hover.Pinned);
    }

    [TestMethod]
    public void ClickingAPinnedGaugeAgainLetsItGo()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        hover.TogglePin(ProviderId.Claude);
        hover.Rail(false);
        hover.TogglePin(ProviderId.Claude);
        clock.Advance(PastDismiss);

        Assert.IsNull(hover.Pinned);
        Assert.IsNull(hover.Expanded);
    }

    [TestMethod]
    public void APinnedCardIsNotStolenByHoveringAnotherGauge()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        hover.TogglePin(ProviderId.Claude);

        hover.Gauge(ProviderId.Codex, true);
        clock.Advance(PastOpen);

        // macOS lets the hover win the card while the pin holds the overlay open.
        Assert.AreEqual(ProviderId.Codex, hover.Expanded);
        Assert.AreEqual(ProviderId.Claude, hover.Pinned);
    }

    [TestMethod]
    public void TheOverlayShrinksToItsTabAfterSittingIdle()
    {
        var (hover, clock) = Build();
        hover.Rail(true);
        hover.Rail(false);
        clock.Advance(PastIdle);

        Assert.IsTrue(hover.IdleMini);
    }

    [TestMethod]
    public void AnOpenCardKeepsTheOverlayFromShrinking()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        clock.Advance(PastOpen);
        clock.Advance(PastIdle);

        Assert.IsFalse(hover.IdleMini);
    }

    [TestMethod]
    public void APinnedCardKeepsTheOverlayFromShrinking()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        hover.TogglePin(ProviderId.Claude);
        hover.Rail(false);
        clock.Advance(PastDismiss);
        clock.Advance(PastIdle);

        Assert.IsFalse(hover.IdleMini);
    }

    [TestMethod]
    public void FindingTheTabBringsTheRailStraightBack()
    {
        var (hover, clock) = Build();
        hover.Rail(true);
        hover.Rail(false);
        clock.Advance(PastIdle);
        Assert.IsTrue(hover.IdleMini);

        hover.RevealFromMini();
        Assert.IsFalse(hover.IdleMini);
    }

    [TestMethod]
    public void EscapeLetsGoOfAPinnedCard()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        hover.TogglePin(ProviderId.Claude);
        hover.Rail(false);

        hover.Unpin();
        clock.Advance(PastDismiss);

        Assert.IsNull(hover.Pinned);
        Assert.IsNull(hover.Expanded);
    }

    [TestMethod]
    public void HidingTheOverlayForgetsEverythingAndLeavesNoTimersBehind()
    {
        var (hover, clock) = Build();
        hover.Gauge(ProviderId.Claude, true);
        hover.TogglePin(ProviderId.Claude);
        hover.Rail(false);

        hover.Reset();

        Assert.IsNull(hover.Expanded);
        Assert.IsNull(hover.Pinned);
        Assert.IsFalse(hover.IdleMini);
        Assert.AreEqual(0, clock.Outstanding, "a cancelled delay should not still be pending");
    }

    [TestMethod]
    public void EveryChangeIsAnnouncedOnce()
    {
        var (hover, clock) = Build();
        var changes = 0;
        hover.Changed += (_, _) => changes++;

        hover.Gauge(ProviderId.Claude, true);
        clock.Advance(PastOpen);
        Assert.AreEqual(1, changes);

        // Opening the same card again is not a change.
        hover.Gauge(ProviderId.Claude, true);
        clock.Advance(PastOpen);
        Assert.AreEqual(1, changes);
    }
}
