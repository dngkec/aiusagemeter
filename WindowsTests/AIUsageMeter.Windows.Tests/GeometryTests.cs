using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Tests;

[TestClass]
public sealed class SquircleTests
{
    private static readonly Rect Box = new(0, 0, 200, 200);

    private static bool Inside(Geometry geometry, double x, double y)
        => geometry.FillContains(new Point(x, y), 0.001, ToleranceType.Absolute);

    [TestMethod]
    public void ACornerReachesPastItsOwnRadius()
    {
        // 1.6 * 44 = 70.4, which is what lets the rail keep a 44pt corner inside 72pt of width.
        Assert.AreEqual(70.4, Squircle.Reach(44), 1e-9);
        Assert.AreEqual(44d, Squircle.Reach(44, smoothing: 0), 1e-9);
    }

    [TestMethod]
    public void TheOutlineIsClosedAndFrozen()
    {
        var geometry = Squircle.RoundedRect(Box, CornerRadii.Uniform(40));

        Assert.IsTrue(geometry.IsFrozen);
        // A closed outline has an interior; an open one would not contain its own centre.
        Assert.IsTrue(Inside(geometry, 100, 100));
    }

    [TestMethod]
    public void TheOutlineFillsTheRequestedRectangle()
    {
        var bounds = Squircle.RoundedRect(new Rect(10, 20, 200, 300), CornerRadii.Uniform(40)).Bounds;

        Assert.AreEqual(10d, bounds.X, 0.01);
        Assert.AreEqual(20d, bounds.Y, 0.01);
        Assert.AreEqual(200d, bounds.Width, 0.01);
        Assert.AreEqual(300d, bounds.Height, 0.01);
    }

    [TestMethod]
    public void AZeroRadiusIsAPlainRectangle()
    {
        var geometry = Squircle.RoundedRect(Box, CornerRadii.Uniform(0));

        Assert.IsTrue(Inside(geometry, 0.5, 0.5));
        Assert.IsTrue(Inside(geometry, 199.5, 0.5));
        Assert.IsTrue(Inside(geometry, 199.5, 199.5));
        Assert.IsTrue(Inside(geometry, 0.5, 199.5));
    }

    [TestMethod]
    public void ARoundedCornerCutsTheCornerOffAndASquareOneDoesNot()
    {
        // The rail's shape: leading corners rounded, trailing edge straight.
        var rail = Squircle.RoundedRect(new Rect(0, 0, 72, 400), CornerRadii.Left(44));

        Assert.IsFalse(Inside(rail, 1, 1), "the top-left corner should be rounded away");
        Assert.IsFalse(Inside(rail, 1, 399), "the bottom-left corner should be rounded away");
        Assert.IsTrue(Inside(rail, 71, 1), "the top-right corner should stay square");
        Assert.IsTrue(Inside(rail, 71, 399), "the bottom-right corner should stay square");
    }

    [TestMethod]
    public void TheCornerIsSymmetricAboutItsDiagonal()
    {
        // The second half of each corner is the first mirrored, so containment must mirror too.
        var geometry = Squircle.RoundedRect(Box, CornerRadii.Uniform(60));

        for (var x = 1; x < 60; x += 3)
            for (var y = 1; y < 60; y += 3)
                Assert.AreEqual(Inside(geometry, x, y), Inside(geometry, y, x), $"({x},{y}) versus ({y},{x})");
    }

    [TestMethod]
    public void AContinuousCornerLeavesItsEdgeSoonerThanACircularOne()
    {
        // Both corners share an arc centre, so they meet on the diagonal. What separates them is the
        // run-up: a continuous corner starts bending 1.6 radii out from the corner instead of 1, so
        // close to the edge it has already pulled in while the circular one is still flush.
        var squircle = Squircle.RoundedRect(Box, CornerRadii.Uniform(60));
        var circular = Squircle.RoundedRect(Box, CornerRadii.Uniform(60), smoothing: 0);

        Assert.IsTrue(Inside(circular, 57, 0.5), "the circular corner is still flush with the edge here");
        Assert.IsFalse(Inside(squircle, 57, 0.5), "the continuous corner has already begun to bend");
    }

    [TestMethod]
    public void ACornerTooBigForItsBoxIsClampedRatherThanSpillingOut()
    {
        var bounds = Squircle.RoundedRect(new Rect(0, 0, 40, 40), CornerRadii.Uniform(500)).Bounds;

        Assert.AreEqual(0d, bounds.X, 0.01);
        Assert.AreEqual(0d, bounds.Y, 0.01);
        Assert.AreEqual(40d, bounds.Width, 0.01);
        Assert.AreEqual(40d, bounds.Height, 0.01);
    }

    [TestMethod]
    public void CircularSmoothingMatchesAnActualCircle()
    {
        // With no smoothing every corner is a true quarter circle, so the inscribed disc fits exactly.
        var geometry = Squircle.RoundedRect(Box, CornerRadii.Uniform(100), smoothing: 0);
        var disc = new EllipseGeometry(new Point(100, 100), 100, 100);

        foreach (var angle in Enumerable.Range(0, 72).Select(i => i * Math.PI / 36))
        {
            var point = new Point(100 + 99.5 * Math.Cos(angle), 100 + 99.5 * Math.Sin(angle));
            Assert.IsTrue(geometry.FillContains(point, 0.001, ToleranceType.Absolute), $"inside at {angle:F2}");
            Assert.IsTrue(disc.FillContains(point, 0.001, ToleranceType.Absolute), $"disc at {angle:F2}");
        }
    }
}

[TestClass]
public sealed class CardGeometryTests
{
    private const double Corner = 22;
    private const double TailWidth = 26;
    private const double TailHeight = 30;
    private static readonly Rect Box = new(0, 0, 274, 200);   // cardWidth 248 + tailWidth 26

    private static bool Inside(Geometry geometry, double x, double y)
        => geometry.FillContains(new Point(x, y), 0.001, ToleranceType.Absolute);

    private static Geometry Card(double tailCentre)
        => CardGeometry.Create(Box, Corner, TailWidth, TailHeight, tailCentre);

    [TestMethod]
    public void TheCardFillsItsBoxExceptForTheRoundedTipOfTheTail()
    {
        var bounds = Card(100).Bounds;

        Assert.AreEqual(0d, bounds.X, 0.01);
        Assert.AreEqual(200d, bounds.Height, 0.01);
        // The tail is a quadratic whose control point sits on the right edge, so its widest point is
        // 0.25*268.39 + 0.5*274 + 0.25*268.39, a shade under the box it is laid out in.
        Assert.AreEqual(271.195, bounds.Right, 0.01);
    }

    [TestMethod]
    public void TheOutlineIsClosedAndFrozen()
    {
        var card = Card(100);
        Assert.IsTrue(card.IsFrozen);
        Assert.IsTrue(Inside(card, 120, 100));
    }

    [TestMethod]
    public void TheTailReachesTheTrailingEdgeBesideItsGauge()
    {
        var card = Card(100);

        Assert.IsTrue(Inside(card, 270, 100), "the tail should reach the trailing edge at its centre");
        Assert.IsFalse(Inside(card, 270, 20), "and nowhere else along that edge");
        Assert.IsFalse(Inside(card, 270, 180));
        Assert.IsFalse(Inside(card, 272, 100), "the tip stops just short of the box");
    }

    [TestMethod]
    public void TheTailFollowsItsGaugeUpAndDown()
    {
        Assert.IsTrue(Inside(Card(60), 270, 60));
        Assert.IsFalse(Inside(Card(60), 270, 140));

        Assert.IsTrue(Inside(Card(140), 270, 140));
        Assert.IsFalse(Inside(Card(140), 270, 60));
    }

    [TestMethod]
    public void TheBodyStopsWhereTheTailBegins()
    {
        var card = Card(100);

        // Away from the tail, the card ends a tail-width short of the full bounds.
        Assert.IsTrue(Inside(card, 246, 40));
        Assert.IsFalse(Inside(card, 250, 40));
    }

    [TestMethod]
    public void TheTailIsKeptClearOfTheRoundedCorners()
    {
        // Asked for a tail above the top corner, it settles at the highest point it may occupy.
        var top = Card(-500);
        Assert.IsTrue(Inside(top, 270, Corner + TailHeight / 2));
        Assert.IsFalse(Inside(top, 270, 1));

        var bottom = Card(5000);
        Assert.IsTrue(Inside(bottom, 270, 200 - Corner - TailHeight / 2));
        Assert.IsFalse(Inside(bottom, 270, 199));
    }

    [TestMethod]
    public void TheLeadingCornersAreRounded()
    {
        var card = Card(100);

        Assert.IsFalse(Inside(card, 1, 1));
        Assert.IsFalse(Inside(card, 1, 199));
        Assert.IsTrue(Inside(card, Corner, Corner));
    }
}

[TestClass]
public sealed class RingGeometryTests
{
    private static readonly Point Centre = new(100, 100);
    private const double Radius = 40;

    private static Rect Bounds(double fraction) => RingGeometry.Arc(Centre, Radius, fraction).Bounds;

    [TestMethod]
    public void NoUsageDrawsNothing()
    {
        Assert.IsTrue(RingGeometry.Arc(Centre, Radius, 0).IsEmpty());
        Assert.IsTrue(RingGeometry.Arc(Centre, Radius, -1).IsEmpty());
    }

    [TestMethod]
    public void FullUsageDrawsTheWholeCircle()
    {
        foreach (var fraction in new[] { 1d, 1.5 })
        {
            var bounds = Bounds(fraction);
            Assert.AreEqual(60d, bounds.Left, 0.01, $"left at {fraction}");
            Assert.AreEqual(60d, bounds.Top, 0.01, $"top at {fraction}");
            Assert.AreEqual(80d, bounds.Width, 0.01, $"width at {fraction}");
            Assert.AreEqual(80d, bounds.Height, 0.01, $"height at {fraction}");
        }
    }

    [TestMethod]
    public void AQuarterRunsFromTwelveOClockToThree()
    {
        var bounds = Bounds(0.25);

        Assert.AreEqual(100d, bounds.Left, 0.01);    // never crosses to the left of centre
        Assert.AreEqual(60d, bounds.Top, 0.01);      // starts at the top of the circle
        Assert.AreEqual(140d, bounds.Right, 0.01);   // ends at three o'clock
        Assert.AreEqual(100d, bounds.Bottom, 0.01);  // never drops below centre
    }

    [TestMethod]
    public void AHalfCoversTheTrailingSideOnly()
    {
        var bounds = Bounds(0.5);

        Assert.AreEqual(100d, bounds.Left, 0.01);
        Assert.AreEqual(60d, bounds.Top, 0.01);
        Assert.AreEqual(140d, bounds.Right, 0.01);
        Assert.AreEqual(140d, bounds.Bottom, 0.01);
    }

    [TestMethod]
    public void MoreThanHalfSweepsTheLongWayRound()
    {
        // Three quarters reaches nine o'clock, which needs the large-arc flag set.
        var bounds = Bounds(0.75);

        Assert.AreEqual(60d, bounds.Left, 0.01);
        Assert.AreEqual(60d, bounds.Top, 0.01);
        Assert.AreEqual(140d, bounds.Right, 0.01);
        Assert.AreEqual(140d, bounds.Bottom, 0.01);
    }

    [TestMethod]
    public void ASliverStaysInTheTopTrailingQuadrant()
    {
        // Ten percent is 36 degrees: across to sin 36 and down to 1 - cos 36 of the radius.
        var bounds = Bounds(0.1);

        Assert.AreEqual(100d, bounds.Left, 0.01);
        Assert.AreEqual(60d, bounds.Top, 0.01);
        Assert.AreEqual(100 + Radius * Math.Sin(Math.PI / 5), bounds.Right, 0.01);
        Assert.AreEqual(100 - Radius * Math.Cos(Math.PI / 5), bounds.Bottom, 0.01);
    }

    [TestMethod]
    public void ArcsAreFrozenSoTheRailCanShareThem()
        => Assert.IsTrue(RingGeometry.Arc(Centre, Radius, 0.42).IsFrozen);
}
