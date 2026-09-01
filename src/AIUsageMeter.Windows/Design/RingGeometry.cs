using System.Windows.Media;

namespace AIUsageMeter.Windows.Design;

/// <summary>
/// Arcs for the gauge ring.
/// </summary>
/// <remarks>
/// <para>
/// macOS draws these with <c>Circle().trim(from:to:)</c> rotated a quarter turn. The Windows build
/// previously faked the same thing with a <c>StrokeDashArray</c> on an <c>Ellipse</c>, which was
/// wrong three ways: the diameter and stroke width were hardcoded in both the view model and the
/// XAML so a resize desynchronised them; <c>StrokeDashCap="Round"</c> added half a stroke at each
/// end, so nought percent still painted a dot and a hundred overlapped itself; and the -90 degree
/// correction assumed a start angle for <c>EllipseGeometry</c> that nothing verified.
/// </para>
/// <para>A real arc removes all three questions instead of answering them.</para>
/// </remarks>
internal static class RingGeometry
{
    /// <summary>
    /// The given fraction of a circle, starting at twelve o'clock and running clockwise.
    /// </summary>
    /// <param name="fraction">Nought draws nothing; one or more draws the whole circle.</param>
    public static Geometry Arc(Point centre, double radius, double fraction)
    {
        if (fraction <= 0 || radius <= 0 || double.IsNaN(fraction)) return Geometry.Empty;

        if (fraction >= 1)
        {
            var circle = new EllipseGeometry(centre, radius, radius);
            circle.Freeze();
            return circle;
        }

        var sweep = fraction * 2 * Math.PI;
        var start = new Point(centre.X, centre.Y - radius);
        var end = new Point(centre.X + radius * Math.Sin(sweep), centre.Y - radius * Math.Cos(sweep));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            // A half turn leaves the large-arc flag ambiguous; the sweep direction settles it.
            context.ArcTo(end, new Size(radius, radius), rotationAngle: 0, isLargeArc: fraction > 0.5,
                SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }
}
