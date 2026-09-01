using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Tests;

[TestClass]
public sealed class ProviderGlyphTests
{
    private static readonly Rect UnitBox = new(0, 0, 1, 1);

    [TestMethod]
    public void EveryProviderHasAMark()
    {
        foreach (var id in ProviderInfo.All)
            Assert.IsNotNull(ProviderGlyphs.For(id), id.ToString());
    }

    [TestMethod]
    public void TheSixIdentityMarksAreDrawnRatherThanLettered()
    {
        foreach (var id in new[]
                 {
                     ProviderId.Claude, ProviderId.Codex, ProviderId.OpenAIAPI, ProviderId.Cursor,
                     ProviderId.Grok, ProviderId.Copilot, ProviderId.Gemini
                 })
            Assert.IsInstanceOfType<ProviderGlyph.Vector>(ProviderGlyphs.For(id), id.ToString());
    }

    [TestMethod]
    public void CodexAndTheOpenAiApiShareOneMark()
        => Assert.AreSame(ProviderGlyphs.For(ProviderId.Codex), ProviderGlyphs.For(ProviderId.OpenAIAPI));

    [TestMethod]
    public void ProvidersWithoutAMarkFallBackToTheirMonogram()
    {
        var kimi = ProviderGlyphs.For(ProviderId.Kimi);
        Assert.IsInstanceOfType<ProviderGlyph.Letters>(kimi);
        Assert.AreEqual("K", ((ProviderGlyph.Letters)kimi).Text);
    }

    [TestMethod]
    public void OnlyTheProvidersMacOsLettersAreLettered()
    {
        // macOS falls back to a monogram for exactly these four. Everything else earns a mark.
        var lettered = ProviderInfo.All
            .Where(id => ProviderGlyphs.For(id) is ProviderGlyph.Letters)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { ProviderId.Kimi, ProviderId.Zai, ProviderId.JetBrainsAI, ProviderId.Kilo },
            lettered);
    }

    [TestMethod]
    public void EverySegoeCodePointFallsInTheIconFontsPrivateUseArea()
    {
        // Read off a rendered sheet, not recalled; this guards against a typo putting a letter there.
        foreach (var icon in ProviderInfo.All.Select(ProviderGlyphs.For).OfType<ProviderGlyph.Icon>())
        {
            Assert.IsGreaterThanOrEqualTo(0xE000, icon.Codepoint, $"U+{icon.Codepoint:X4}");
            Assert.IsLessThanOrEqualTo(0xF8FF, icon.Codepoint, $"U+{icon.Codepoint:X4}");
            Assert.AreEqual(icon.Codepoint, icon.Fallback, "the Windows 10 font agrees on these");
        }
    }

    [TestMethod]
    public void EveryDrawnMarkIsFrozenSoTheWholeRailCanShareIt()
    {
        foreach (var layer in AllLayers())
            Assert.IsTrue(layer.Path.IsFrozen);
    }

    [TestMethod]
    public void EveryFilledMarkStaysInsideTheUnitBox()
    {
        foreach (var layer in AllLayers().Where(x => x.StrokeWidth == 0))
        {
            var bounds = layer.Path.Bounds;
            Assert.IsTrue(UnitBox.Contains(bounds), $"{bounds} escapes the unit box");
        }
    }

    [TestMethod]
    public void TheKnotIsStrokedAndTheRestAreFilled()
    {
        var knot = (ProviderGlyph.Vector)ProviderGlyphs.Knot;
        Assert.AreEqual(0.100, knot.Layers.Single().StrokeWidth, 1e-9);

        var burst = (ProviderGlyph.Vector)ProviderGlyphs.Burst;
        Assert.AreEqual(0d, burst.Layers.Single().StrokeWidth);
    }

    [TestMethod]
    public void TheCubeHasThreeFacesAtDescendingStrength()
    {
        var cube = (ProviderGlyph.Vector)ProviderGlyphs.Cube;
        CollectionAssert.AreEqual(new[] { 1d, 0.88, 0.52 }, cube.Layers.Select(x => x.Opacity).ToArray());
    }

    [TestMethod]
    public void TheVisorsLensesArePunchedOutRatherThanPaintedOver()
    {
        // Both lenses sit inside the capsule, so only an even-odd fill leaves holes where the eyes go.
        var visor = ((ProviderGlyph.Vector)ProviderGlyphs.Visor).Layers.Single().Path;

        Assert.IsTrue(Inside(visor, 0.5, 0.5), "the bar between the lenses is solid");
        Assert.IsFalse(Inside(visor, 0.28, 0.5), "the leading lens should be a hole");
        Assert.IsFalse(Inside(visor, 0.72, 0.5), "the trailing lens should be a hole");
    }

    [TestMethod]
    public void TheSlashCrossesWithAGapWhereItsStrokesMeet()
    {
        // The two bars wind opposite ways, so the crossing cancels and leaves the notch xAI's mark has.
        var slash = ((ProviderGlyph.Vector)ProviderGlyphs.Slash).Layers.Single().Path;

        Assert.IsFalse(Inside(slash, 0.5, 0.5));
        Assert.IsTrue(Inside(slash, 0.12, 0.1), "the leading bar is solid away from the crossing");
    }

    [TestMethod]
    public void TheBurstsCoreStandsClearOfItsRays()
    {
        var burst = ((ProviderGlyph.Vector)ProviderGlyphs.Burst).Layers.Single().Path;

        // Sampled off the ray axes: a point sitting exactly on a triangle's apex is the degenerate
        // case for point-in-polygon, and the crossing count there is not well defined.
        Assert.IsTrue(Inside(burst, 0.5, 0.5), "the core is solid");

        // Past the core, which ends at 0.0825, and short of the rays, which begin at 0.095.
        var (gapX, gapY) = Polar(0.09, 11.25);
        Assert.IsFalse(Inside(burst, gapX, gapY), "there is a gap before the rays begin");

        // Down the centreline of a ray, well past its base.
        var (rayX, rayY) = Polar(0.3, 45);
        Assert.IsTrue(Inside(burst, rayX, rayY), "and a ray beyond it");
    }

    private static (double X, double Y) Polar(double radius, double degrees)
    {
        var angle = degrees * Math.PI / 180;
        return (0.5 + radius * Math.Cos(angle), 0.5 + radius * Math.Sin(angle));
    }

    /// <summary>
    /// Hit tests a unit-box mark at a size it is actually drawn at.
    /// </summary>
    /// <remarks>
    /// WPF hit tests through a fixed-point rasteriser with roughly 1/256 of a device unit of
    /// precision. Asking a one-by-one geometry whether it contains a point resolves nothing finer
    /// than about four thousandths, which is coarser than the gaps these marks are made of, and
    /// answers were wrong in both directions. Scaling first is the difference between measuring the
    /// shape and measuring the rasteriser.
    /// </remarks>
    private static bool Inside(System.Windows.Media.Geometry unitGeometry, double x, double y)
    {
        const double scale = 200;
        var scaled = unitGeometry.Clone();
        scaled.Transform = new ScaleTransform(scale, scale);
        return scaled.GetFlattenedPathGeometry()
            .FillContains(new Point(x * scale, y * scale), 0.01, ToleranceType.Absolute);
    }

    private static IEnumerable<GlyphLayer> AllLayers()
        => ProviderInfo.All
            .Select(ProviderGlyphs.For)
            .OfType<ProviderGlyph.Vector>()
            .SelectMany(x => x.Layers);
}
