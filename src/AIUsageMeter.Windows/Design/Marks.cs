using System.Windows.Media;

namespace AIUsageMeter.Windows.Design;

/// <summary>
/// Small marks the interface needs that are not provider logos.
/// </summary>
/// <remarks>
/// macOS reaches for SF Symbols here. Those are Apple's and cannot ship on Windows, and the icon
/// fonts that do ship have no published code-point list to check a guess against, so these few are
/// drawn. All are built in a one-by-one box and frozen.
/// </remarks>
internal static class Marks
{
    /// <summary>The support heart. Stroked when idle, filled once the pointer is on it.</summary>
    public static Geometry Heart { get; } = BuildHeart();

    /// <summary>The plus inside the setup button.</summary>
    public static Geometry Plus { get; } = BuildPlus();

    /// <summary>Shown in the card's header while its card is being kept open.</summary>
    public static Geometry Pin { get; } = BuildPin();

    /// <summary>A currency mark in a ring, for the pay-as-you-go APIs.</summary>
    public static Geometry Money { get; } = BuildMoney();

    /// <summary>Three waves.</summary>
    public static Geometry Waves { get; } = BuildStrokes(wavy: true);

    /// <summary>Three gusts, tapering off to the trailing side.</summary>
    public static Geometry Wind { get; } = BuildStrokes(wavy: false);

    /// <summary>A trunk splitting into two, for a router.</summary>
    public static Geometry Branch { get; } = BuildBranch();

    /// <summary>A jigsaw piece, for a connector the reader supplies.</summary>
    public static Geometry Puzzle { get; } = BuildPuzzle();

    /// <summary>An arrow leaving a ring, for a service that sends you elsewhere.</summary>
    public static Geometry ArrowOut { get; } = BuildArrowOut();

    private static Geometry BuildHeart()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // Symmetric about the vertical centre line: two lobes meeting at a point below.
            context.BeginFigure(new Point(0.50, 0.92), isFilled: true, isClosed: true);
            context.BezierTo(new Point(0.30, 0.78), new Point(0.02, 0.56), new Point(0.02, 0.33), true, true);
            context.BezierTo(new Point(0.02, 0.14), new Point(0.17, 0.05), new Point(0.30, 0.05), true, true);
            context.BezierTo(new Point(0.40, 0.05), new Point(0.47, 0.11), new Point(0.50, 0.17), true, true);
            context.BezierTo(new Point(0.53, 0.11), new Point(0.60, 0.05), new Point(0.70, 0.05), true, true);
            context.BezierTo(new Point(0.83, 0.05), new Point(0.98, 0.14), new Point(0.98, 0.33), true, true);
            context.BezierTo(new Point(0.98, 0.56), new Point(0.70, 0.78), new Point(0.50, 0.92), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildMoney()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new EllipseGeometry(new Point(0.5, 0.5), 0.44, 0.44));

        var mark = new StreamGeometry();
        using (var context = mark.Open())
        {
            // The upright, then an S through it.
            context.BeginFigure(new Point(0.5, 0.16), isFilled: false, isClosed: false);
            context.LineTo(new Point(0.5, 0.84), true, false);
            context.BeginFigure(new Point(0.66, 0.33), isFilled: false, isClosed: false);
            context.BezierTo(new Point(0.58, 0.25), new Point(0.36, 0.26), new Point(0.36, 0.40), true, true);
            context.BezierTo(new Point(0.36, 0.56), new Point(0.64, 0.50), new Point(0.64, 0.63), true, true);
            context.BezierTo(new Point(0.64, 0.76), new Point(0.42, 0.78), new Point(0.34, 0.69), true, true);
        }

        mark.Freeze();
        group.Children.Add(mark);
        group.Freeze();
        return group;
    }

    private static Geometry BuildStrokes(bool wavy)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index < 3; index++)
            {
                var y = 0.28 + index * 0.22;
                context.BeginFigure(new Point(0.08, y), isFilled: false, isClosed: false);
                if (wavy)
                {
                    context.BezierTo(new Point(0.25, y - 0.13), new Point(0.42, y + 0.13), new Point(0.58, y), true, true);
                    context.BezierTo(new Point(0.72, y - 0.11), new Point(0.82, y + 0.09), new Point(0.92, y), true, true);
                }
                else
                {
                    // A gust: straight, then curling back on itself at the trailing end.
                    var reach = index == 1 ? 0.74 : 0.60;
                    context.LineTo(new Point(reach, y), true, false);
                    context.BezierTo(new Point(reach + 0.16, y), new Point(reach + 0.18, y - 0.18),
                        new Point(reach + 0.02, y - 0.16), true, true);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildBranch()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        var lines = new StreamGeometry();
        using (var context = lines.Open())
        {
            context.BeginFigure(new Point(0.22, 0.18), isFilled: false, isClosed: false);
            context.LineTo(new Point(0.22, 0.62), true, false);
            context.BezierTo(new Point(0.22, 0.80), new Point(0.40, 0.80), new Point(0.78, 0.80), true, true);
            context.BeginFigure(new Point(0.22, 0.44), isFilled: false, isClosed: false);
            context.BezierTo(new Point(0.22, 0.30), new Point(0.44, 0.28), new Point(0.78, 0.28), true, true);
        }

        lines.Freeze();
        group.Children.Add(lines);
        group.Children.Add(new EllipseGeometry(new Point(0.22, 0.14), 0.11, 0.11));
        group.Children.Add(new EllipseGeometry(new Point(0.84, 0.28), 0.11, 0.11));
        group.Children.Add(new EllipseGeometry(new Point(0.84, 0.80), 0.11, 0.11));
        group.Freeze();
        return group;
    }

    private static Geometry BuildPuzzle()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0.10, 0.10), isFilled: true, isClosed: true);
            context.LineTo(new Point(0.40, 0.10), true, false);
            // The tab on the top edge.
            context.BezierTo(new Point(0.36, -0.02), new Point(0.64, -0.02), new Point(0.60, 0.10), true, true);
            context.LineTo(new Point(0.90, 0.10), true, false);
            context.LineTo(new Point(0.90, 0.40), true, false);
            // And the socket on the trailing edge.
            context.BezierTo(new Point(0.78, 0.36), new Point(0.78, 0.64), new Point(0.90, 0.60), true, true);
            context.LineTo(new Point(0.90, 0.90), true, false);
            context.LineTo(new Point(0.10, 0.90), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildArrowOut()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new EllipseGeometry(new Point(0.5, 0.5), 0.44, 0.44));

        var arrow = new StreamGeometry();
        using (var context = arrow.Open())
        {
            context.BeginFigure(new Point(0.34, 0.66), isFilled: false, isClosed: false);
            context.LineTo(new Point(0.66, 0.34), true, false);
            context.BeginFigure(new Point(0.40, 0.34), isFilled: false, isClosed: false);
            context.LineTo(new Point(0.66, 0.34), true, false);
            context.LineTo(new Point(0.66, 0.60), true, false);
        }

        arrow.Freeze();
        group.Children.Add(arrow);
        group.Freeze();
        return group;
    }

    private static Geometry BuildPin()
    {
        // A drawing pin seen head on: a round head, a collar, and a point below.
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new EllipseGeometry(new Point(0.5, 0.34), 0.30, 0.30));
        group.Children.Add(new RectangleGeometry(new Rect(0.44, 0.60, 0.12, 0.38), 0.06, 0.06));
        group.Freeze();
        return group;
    }

    private static Geometry BuildPlus()
    {
        const double arm = 0.42;
        const double half = 0.075;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new RectangleGeometry(new Rect(0.5 - arm, 0.5 - half, arm * 2, half * 2), half, half));
        group.Children.Add(new RectangleGeometry(new Rect(0.5 - half, 0.5 - arm, half * 2, arm * 2), half, half));
        group.Freeze();
        return group;
    }
}
