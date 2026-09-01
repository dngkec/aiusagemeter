using System.Windows;
using System.Windows.Media;

namespace AIUsageMeter.Windows.Design;

/// <summary>Corner radii, clockwise from the top left.</summary>
internal readonly record struct CornerRadii(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
{
    public static CornerRadii Uniform(double radius) => new(radius, radius, radius, radius);

    /// <summary>Rounded down the leading edge only, as the rail and the mini tab are.</summary>
    public static CornerRadii Left(double radius) => new(radius, 0, 0, radius);
}

/// <summary>
/// A rounded rectangle with Apple's continuous corners.
/// </summary>
/// <remarks>
/// <para>
/// macOS asks for <c>RoundedRectangle(style: .continuous)</c> on the rail and the mini tab. At the
/// rail's 44pt radius a circular corner is plainly the wrong shape, so a <c>CornerRadius</c> will
/// not do.
/// </para>
/// <para>
/// Each corner is three segments: a cubic Bézier easing off the straight edge, a shortened circular
/// arc through the diagonal, and a second cubic mirrored across that diagonal. <c>smoothing</c> sets
/// how much of the 90 degrees the Béziers take from the arc; Apple's shape is close to 0.6, and at 0
/// this degenerates to an ordinary circular rounded rectangle.
/// </para>
/// <para>
/// The arc keeps the circular corner's own centre, so the two shapes meet on the diagonal. What
/// differs is the run-up: a continuous corner starts bending 1.6 radii out instead of 1.
/// </para>
/// <para>
/// Apple has never published the real curve. This is an approximation, and the residual against the
/// reference screenshot is recorded when the snapshot diff is calibrated.
/// </para>
/// </remarks>
internal static class Squircle
{
    /// <summary>How much corner smoothing Apple's continuous corner appears to use.</summary>
    public const double AppleSmoothing = 0.6;

    private static readonly Vector Up = new(0, -1);
    private static readonly Vector Down = new(0, 1);
    private static readonly Vector Left = new(-1, 0);
    private static readonly Vector Right = new(1, 0);

    /// <summary>
    /// How far a corner of this radius reaches along each of its edges. The rail depends on this
    /// staying inside its 72pt width: 1.6 * 44 is 70.4, which just fits.
    /// </summary>
    public static double Reach(double radius, double smoothing = AppleSmoothing) => (1 + smoothing) * radius;

    public static Geometry RoundedRect(Rect bounds, CornerRadii radii, double smoothing = AppleSmoothing)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return Geometry.Empty;
        smoothing = Math.Clamp(smoothing, 0, 1);

        var width = bounds.Width;
        var height = bounds.Height;
        var topLeft = Fit(radii.TopLeft, width, radii.TopRight, height, radii.BottomLeft, smoothing);
        var topRight = Fit(radii.TopRight, width, radii.TopLeft, height, radii.BottomRight, smoothing);
        var bottomRight = Fit(radii.BottomRight, width, radii.BottomLeft, height, radii.TopRight, smoothing);
        var bottomLeft = Fit(radii.BottomLeft, width, radii.BottomRight, height, radii.TopLeft, smoothing);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // Clockwise from the top-left corner, which we arrive at travelling up the leading edge.
            context.BeginFigure(Entry(bounds.TopLeft, Up, topLeft, smoothing), isFilled: true, isClosed: true);
            Corner(context, bounds.TopLeft, Up, Right, topLeft, smoothing);
            Corner(context, bounds.TopRight, Right, Down, topRight, smoothing);
            Corner(context, bounds.BottomRight, Down, Left, bottomRight, smoothing);
            Corner(context, bounds.BottomLeft, Left, Up, bottomLeft, smoothing);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Shrinks a radius until its reach fits the edges it shares. Each edge is split between its two
    /// corners in proportion to their radii, so a corner beside a square one gets the whole edge —
    /// which is what lets the rail keep its 44pt corner across only 72pt of width.
    /// </summary>
    private static double Fit(double radius, double width, double acrossTop, double height, double downSide, double smoothing)
    {
        if (radius <= 0) return 0;
        var budget = Math.Min(Share(width, radius, acrossTop), Share(height, radius, downSide));
        return Math.Min(radius, budget / (1 + smoothing));
    }

    private static double Share(double edge, double mine, double other)
        => mine + other <= 0 ? 0 : edge * mine / (mine + other);

    private static Point Entry(Point corner, Vector arriving, double radius, double smoothing)
        => corner - arriving * Reach(radius, smoothing);

    /// <summary>
    /// Draws one corner, having travelled in along <paramref name="arriving"/> and leaving along
    /// <paramref name="leaving"/>. Both vectors point the way the outline is being traced.
    /// </summary>
    private static void Corner(StreamGeometryContext context, Point corner, Vector arriving, Vector leaving, double radius, double smoothing)
    {
        if (radius <= 0)
        {
            context.LineTo(corner, isStroked: true, isSmoothJoin: false);
            return;
        }

        var part = Parts(radius, smoothing);
        var straight = part.P - part.A - part.B - part.C;

        context.LineTo(corner - arriving * part.P, isStroked: true, isSmoothJoin: false);
        context.BezierTo(
            corner - arriving * (part.P - part.A),
            corner - arriving * (part.P - part.A - part.B),
            corner - arriving * straight + leaving * part.D,
            isStroked: true, isSmoothJoin: true);
        context.ArcTo(
            corner - arriving * part.D + leaving * straight,
            new Size(radius, radius), rotationAngle: 0, isLargeArc: false,
            SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
        context.BezierTo(
            corner + leaving * (part.P - part.A - part.B),
            corner + leaving * (part.P - part.A),
            corner + leaving * part.P,
            isStroked: true, isSmoothJoin: true);
    }

    /// <summary>
    /// The lengths one corner is built from, measured along its edges from the corner point.
    /// </summary>
    /// <remarks>
    /// <c>A</c>, <c>B</c>, <c>C</c>, <c>D</c> and <c>ArcSection</c> sum to <c>P</c> by construction,
    /// which is what makes the mirrored second Bézier meet the arc exactly.
    /// </remarks>
    private readonly record struct CornerParts(double P, double A, double B, double C, double D, double ArcSection);

    private static CornerParts Parts(double radius, double smoothing)
    {
        var reach = Reach(radius, smoothing);
        var arcMeasure = 90 * (1 - smoothing);
        // The arc is symmetric about the diagonal, so this is its extent along each axis, not its chord.
        var arcSection = Math.Sin(Radians(arcMeasure / 2)) * radius * Math.Sqrt(2);
        var alpha = (90 - arcMeasure) / 2;
        var tangent = radius * Math.Tan(Radians(alpha / 2));
        var c = tangent * Math.Cos(Radians(alpha));
        var d = c * Math.Tan(Radians(alpha));
        var b = (reach - arcSection - c - d) / 3;
        return new(reach, 2 * b, b, c, d, arcSection);
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}

/// <summary>
/// The detail card: a rounded body with a tail on its trailing edge pointing back at its gauge.
/// </summary>
/// <remarks>
/// A port of <c>CardShape</c> in <c>Sources/AIUsageMeter/NotchViews.swift</c>. Unlike the rail, the
/// macOS original draws ordinary circular corners here, and that difference is preserved.
/// </remarks>
internal static class CardGeometry
{
    /// <summary>
    /// Builds the card outline. <paramref name="tailCentre"/> is measured down from the top of
    /// <paramref name="bounds"/> and is clamped so the tail stays clear of the rounded corners.
    /// </summary>
    public static Geometry Create(Rect bounds, double corner, double tailWidth, double tailHeight, double tailCentre)
    {
        if (bounds.Width <= tailWidth || bounds.Height <= 0) return Geometry.Empty;

        var radius = Math.Max(0, Math.Min(corner, Math.Min(bounds.Width - tailWidth, bounds.Height) / 2));
        var bodyRight = bounds.Right - tailWidth;
        var half = Math.Max(0, tailHeight / 2);
        var aim = Math.Clamp(tailCentre, radius + half, Math.Max(radius + half, bounds.Height - radius - half));
        var centre = bounds.Top + aim;
        var tip = tailHeight * 0.11;
        var arc = new Size(radius, radius);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(bounds.Left + radius, bounds.Top), isFilled: true, isClosed: true);

            context.LineTo(new Point(bodyRight - radius, bounds.Top), true, false);
            context.ArcTo(new Point(bodyRight, bounds.Top + radius), arc, 0, false, SweepDirection.Clockwise, true, true);

            // The tail: out to a rounded point on the trailing edge and back.
            context.LineTo(new Point(bodyRight, centre - half), true, false);
            context.LineTo(new Point(bounds.Right - tip * 1.7, centre - tip * 0.85), true, false);
            context.QuadraticBezierTo(
                new Point(bounds.Right, centre),
                new Point(bounds.Right - tip * 1.7, centre + tip * 0.85),
                true, true);
            context.LineTo(new Point(bodyRight, centre + half), true, false);

            context.LineTo(new Point(bodyRight, bounds.Bottom - radius), true, false);
            context.ArcTo(new Point(bodyRight - radius, bounds.Bottom), arc, 0, false, SweepDirection.Clockwise, true, true);
            context.LineTo(new Point(bounds.Left + radius, bounds.Bottom), true, false);
            context.ArcTo(new Point(bounds.Left, bounds.Bottom - radius), arc, 0, false, SweepDirection.Clockwise, true, true);
            context.LineTo(new Point(bounds.Left, bounds.Top + radius), true, false);
            context.ArcTo(new Point(bounds.Left + radius, bounds.Top), arc, 0, false, SweepDirection.Clockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }
}
