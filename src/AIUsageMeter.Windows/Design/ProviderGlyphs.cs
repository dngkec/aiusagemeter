using System.Windows.Media;
using AIUsageMeter.Core;

namespace AIUsageMeter.Windows.Design;

/// <summary>
/// One drawing pass of a mark, in a one-by-one box the renderer scales to the size it needs.
/// </summary>
/// <param name="Path">Frozen, so every gauge on the rail shares it.</param>
/// <param name="Opacity">The cube's three faces are the same colour at different strengths.</param>
/// <param name="StrokeWidth">Nought fills the path; anything else strokes it, in unit-box terms.</param>
internal sealed record GlyphLayer(Geometry Path, double Opacity = 1, double StrokeWidth = 0);

/// <summary>How one provider's mark is drawn.</summary>
internal abstract record ProviderGlyph
{
    private ProviderGlyph() { }

    /// <summary>Drawn by this app, as macOS draws it, so no vendor artwork is redistributed.</summary>
    public sealed record Vector(IReadOnlyList<GlyphLayer> Layers) : ProviderGlyph;

    /// <summary>
    /// A character from Segoe Fluent Icons, standing in for an SF Symbol that cannot be shipped.
    /// <paramref name="Fallback"/> is the Segoe MDL2 Assets code point for Windows 10.
    /// </summary>
    public sealed record Icon(int Codepoint, int Fallback) : ProviderGlyph;

    /// <summary>Letters, for providers with no mark of their own.</summary>
    public sealed record Letters(string Text) : ProviderGlyph;
}

/// <summary>
/// Provider marks, mirroring <c>Sources/AIUsageMeter/Glyphs.swift</c>.
/// </summary>
/// <remarks>
/// macOS draws six marks itself and reaches for SF Symbols for the rest. SF Symbols is Apple's and
/// has no Windows equivalent, so the six drawn marks are ported exactly and the remainder fall back
/// to letters until each is either drawn or mapped to a verified Segoe code point.
/// </remarks>
internal static class ProviderGlyphs
{
    public static ProviderGlyph For(ProviderId id) => id switch
    {
        // Drawn: the six identity marks macOS draws, plus the handful of SF Symbols with no
        // counterpart anyone could point at in an icon font.
        ProviderId.Claude => Burst,
        ProviderId.Codex or ProviderId.OpenAIAPI => Knot,
        ProviderId.Cursor => Cube,
        ProviderId.Grok => Slash,
        ProviderId.Copilot => Visor,
        ProviderId.Gemini => Spark,
        ProviderId.AnthropicCost or ProviderId.XaiAPI => Money,
        ProviderId.DeepSeek => Waves,
        ProviderId.Mistral => Wind,
        ProviderId.OpenRouter => Branch,
        ProviderId.Antigravity => ArrowOut,
        ProviderId.Custom => Puzzle,

        // Segoe: every one of these code points was read off a rendered sheet of the font, not
        // recalled. See WindowsTests IconSheet for how to regenerate it.
        ProviderId.Moonshot => Symbol(0xE708),        // crescent moon
        ProviderId.Perplexity => Symbol(0xE721),      // magnifier
        ProviderId.Warp => Symbol(0xE756),            // console
        ProviderId.Devin => Symbol(0xE776),           // standing figure
        ProviderId.Augment => Symbol(0xE794),         // wand and sparkles
        ProviderId.Windsurf => Symbol(0xE7E3),        // boat
        ProviderId.OpenCode => Symbol(0xE943),        // braces
        ProviderId.Amp => Symbol(0xE945),             // lightning bolt
        ProviderId.LocalModels => Symbol(0xE950),     // processor

        // Letters, exactly where macOS uses them.
        _ => new ProviderGlyph.Letters(id.Monogram())
    };

    /// <summary>
    /// A Segoe code point. Windows 10 ships Segoe MDL2 Assets rather than Segoe Fluent Icons, and
    /// the two agree on the code points used here, so one value serves both.
    /// </summary>
    private static ProviderGlyph Symbol(int codepoint) => new ProviderGlyph.Icon(codepoint, codepoint);

    public static ProviderGlyph Burst { get; } = new ProviderGlyph.Vector([new(BuildBurst())]);
    public static ProviderGlyph Knot { get; } = new ProviderGlyph.Vector([new(BuildKnot(), StrokeWidth: 0.100)]);
    public static ProviderGlyph Slash { get; } = new ProviderGlyph.Vector([new(BuildSlash())]);
    public static ProviderGlyph Spark { get; } = new ProviderGlyph.Vector([new(BuildSpark())]);
    public static ProviderGlyph Visor { get; } = new ProviderGlyph.Vector([new(BuildVisor())]);

    public static ProviderGlyph Money { get; } = new ProviderGlyph.Vector([new(Marks.Money, StrokeWidth: 0.085)]);
    public static ProviderGlyph Waves { get; } = new ProviderGlyph.Vector([new(Marks.Waves, StrokeWidth: 0.095)]);
    public static ProviderGlyph Wind { get; } = new ProviderGlyph.Vector([new(Marks.Wind, StrokeWidth: 0.095)]);
    public static ProviderGlyph Branch { get; } = new ProviderGlyph.Vector([new(Marks.Branch, StrokeWidth: 0.085)]);
    public static ProviderGlyph Puzzle { get; } = new ProviderGlyph.Vector([new(Marks.Puzzle, StrokeWidth: 0.085)]);
    public static ProviderGlyph ArrowOut { get; } = new ProviderGlyph.Vector([new(Marks.ArrowOut, StrokeWidth: 0.085)]);

    /// <summary>Three faces of one cube, lit by opacity rather than by three colours.</summary>
    public static ProviderGlyph Cube { get; } = new ProviderGlyph.Vector(
    [
        new(BuildFace([(0.50, 0.04), (0.94, 0.29), (0.50, 0.54), (0.06, 0.29)])),
        new(BuildFace([(0.06, 0.29), (0.50, 0.54), (0.50, 0.96), (0.06, 0.71)]), Opacity: 0.88),
        new(BuildFace([(0.94, 0.29), (0.94, 0.71), (0.50, 0.96), (0.50, 0.54)]), Opacity: 0.52)
    ]);

    private const double Centre = 0.5;

    /// <summary>Sixteen rays and a detached core, as Claude's mark is drawn on macOS.</summary>
    private static Geometry BuildBurst()
    {
        const int rays = 16;
        const double radius = 0.5;
        const double inner = radius * 0.19;
        const double half = radius * 0.036;

        var spokes = new StreamGeometry();
        using (var context = spokes.Open())
        {
            for (var index = 0; index < rays; index++)
            {
                var angle = (double)index / rays * 2 * Math.PI;
                var along = new System.Windows.Vector(Math.Cos(angle), Math.Sin(angle));
                var across = new System.Windows.Vector(-Math.Sin(angle), Math.Cos(angle));
                Point At(double radial, double lateral) =>
                    new(Centre + radial * along.X + lateral * across.X,
                        Centre + radial * along.Y + lateral * across.Y);

                context.BeginFigure(At(inner, -half), isFilled: true, isClosed: true);
                context.LineTo(At(radius, 0), true, false);
                context.LineTo(At(inner, half), true, false);
            }
        }

        // The core never touches the rays: they start at 0.19 of the radius, it ends at 0.165.
        // An EllipseGeometry rather than a pair of hand-rolled half-arcs, whose large-arc flag is
        // ambiguous at exactly half a turn and which flattened into a lumpy disc.
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(spokes);
        group.Children.Add(new EllipseGeometry(new Point(Centre, Centre), radius * 0.165, radius * 0.165));
        group.Freeze();
        return group;
    }

    /// <summary>Six overlapping lobes around a hexagonal core. Stroked, not filled.</summary>
    private static Geometry BuildKnot()
    {
        const double radius = 0.5 * 0.96;
        const double offset = radius * 0.634;
        const double lobe = radius * 0.366;

        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = geometry.Open())
        {
            for (var index = 0; index < 6; index++)
            {
                var angle = (double)index / 6 * 2 * Math.PI - Math.PI / 2;
                var origin = new Point(Centre + offset * Math.Cos(angle), Centre + offset * Math.Sin(angle));
                var from = Around(origin, lobe, angle - Math.PI / 2);
                var to = Around(origin, lobe, angle + Math.PI / 2);

                if (index == 0) context.BeginFigure(from, isFilled: true, isClosed: true);
                else context.LineTo(from, true, false);

                // Half a turn, so the large-arc flag is ambiguous and the sweep direction decides.
                context.ArcTo(to, new Size(lobe, lobe), 0, false, SweepDirection.Clockwise, true, true);
            }

            const double core = radius * 0.40;
            for (var index = 0; index < 6; index++)
            {
                var angle = (double)index / 6 * 2 * Math.PI - Math.PI / 3;
                var point = Around(new Point(Centre, Centre), core, angle);
                if (index == 0) context.BeginFigure(point, isFilled: true, isClosed: true);
                else context.LineTo(point, true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildSlash()
        => Polygons(
            [(0.04, 0.05), (0.26, 0.05), (0.96, 0.95), (0.74, 0.95)],
            [(0.96, 0.05), (0.74, 0.05), (0.04, 0.95), (0.26, 0.95)]);

    private static Geometry BuildSpark()
    {
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0.5, 0), isFilled: true, isClosed: true);
            context.QuadraticBezierTo(new Point(0.58, 0.42), new Point(1, 0.5), true, true);
            context.QuadraticBezierTo(new Point(0.58, 0.58), new Point(0.5, 1), true, true);
            context.QuadraticBezierTo(new Point(0.42, 0.58), new Point(0, 0.5), true, true);
            context.QuadraticBezierTo(new Point(0.42, 0.42), new Point(0.5, 0), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>A capsule with two lenses punched out of it.</summary>
    private static Geometry BuildVisor()
    {
        const double lens = 0.24;
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        // Corner radius is half the bar's height, so the bar is a capsule.
        group.Children.Add(new RectangleGeometry(new Rect(0, 0.22, 1, 0.56), 0.28, 0.28));
        group.Children.Add(new EllipseGeometry(new Point(0.16 + lens / 2, 0.38 + lens / 2), lens / 2, lens / 2));
        group.Children.Add(new EllipseGeometry(new Point(1 - 0.16 - lens / 2, 0.38 + lens / 2), lens / 2, lens / 2));
        group.Freeze();
        return group;
    }

    private static Geometry BuildFace((double X, double Y)[] corners) => Polygons(corners);

    private static Geometry Polygons(params (double X, double Y)[][] shapes)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = geometry.Open())
        {
            foreach (var shape in shapes)
            {
                context.BeginFigure(new Point(shape[0].X, shape[0].Y), isFilled: true, isClosed: true);
                for (var index = 1; index < shape.Length; index++)
                    context.LineTo(new Point(shape[index].X, shape[index].Y), true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point Around(Point centre, double radius, double angle)
        => new(centre.X + radius * Math.Cos(angle), centre.Y + radius * Math.Sin(angle));
}
