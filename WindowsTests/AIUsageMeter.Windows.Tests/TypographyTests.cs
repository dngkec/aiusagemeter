using System.IO;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Tests;

[TestClass]
public sealed class TypoTests
{
    private static readonly Typo Medium = Typo.For(OverlaySize.Medium);

    [TestMethod]
    public void TheRampMatchesTheMacBuild()
    {
        Assert.AreEqual(16d, Medium.GaugeValue.Size);
        Assert.AreEqual(FontWeights.SemiBold, Medium.GaugeValue.Weight);

        Assert.AreEqual(16d, Medium.CardTitle.Size);
        Assert.AreEqual(FontWeights.Bold, Medium.CardTitle.Weight);

        Assert.AreEqual(12.5, Medium.RowLabel.Size);
        Assert.AreEqual(13d, Medium.RowValue.Size);
        Assert.AreEqual(10.5, Medium.FooterSecondary.Size);
        Assert.AreEqual(7.5, Medium.ActionGlyph.Size);
        Assert.AreEqual(15d, Medium.SetupGlyph.Size);
    }

    [TestMethod]
    public void TypeScalesButIsNotRounded()
    {
        // Metrics rounds after scaling; the macOS type ramp does not, so neither may this.
        Assert.AreEqual(16 * 1.18, Typo.For(OverlaySize.Large).GaugeValue.Size, 1e-9);
        Assert.AreEqual(16 * 0.86, Typo.For(OverlaySize.Small).GaugeValue.Size, 1e-9);
        Assert.AreEqual(12.5 * 1.18, Typo.For(OverlaySize.Large).RowLabel.Size, 1e-9);
    }

    [TestMethod]
    public void TheChangingReadoutsUseTabularFigures()
    {
        Assert.IsTrue(Medium.GaugeValue.TabularDigits, "the rail caption counts up and down in place");
        Assert.IsTrue(Medium.RowValue.TabularDigits, "so does the usage readout");
        Assert.IsFalse(Medium.CardTitle.TabularDigits, "a provider name has no digits to align");
    }

    [TestMethod]
    public void TheFamilyResolvesWithoutAnApplicationObject()
        // The pack scheme is registered by WPF's Application static constructor. Reaching for the
        // font before that has run must not throw, or the type is unusable early in startup.
        => Assert.IsNotNull(Typo.Family);

    [TestMethod]
    public void TheRampOnlyAsksForWeightsThatShip()
    {
        // WPF synthesises a missing weight by smearing the nearest one, and says nothing about it.
        foreach (var size in Enum.GetValues<OverlaySize>())
            foreach (var style in Typo.For(size).All)
                Assert.Contains(style.Weight, Typo.ShippedWeights, $"{style.Size} at {size}");
    }
}

[TestClass]
public sealed class EmbeddedFontTests
{
    private static string FontFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AIUsageMeter.Windows.sln")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "could not find the repository root from the test assembly");
        return Path.Combine(directory.FullName, "src", "AIUsageMeter.Windows", "Assets", "Fonts");
    }

    [TestMethod]
    public void TheFourStaticWeightsAreCheckedIn()
    {
        var folder = FontFolder();
        foreach (var name in new[] { "Inter-Regular.ttf", "Inter-Medium.ttf", "Inter-SemiBold.ttf", "Inter-Bold.ttf" })
            Assert.IsTrue(File.Exists(Path.Combine(folder, name)), name);

        Assert.IsFalse(File.Exists(Path.Combine(folder, "InterVariable.ttf")),
            "WPF cannot render a variable font; shipping one would silently flatten every weight");
    }

    [TestMethod]
    public void TheEmbeddedFilesProvideInterAtEveryWeightTheRampUses()
    {
        var families = Fonts.GetFontFamilies(new Uri(FontFolder() + Path.DirectorySeparatorChar));
        var inter = families.FirstOrDefault(x => x.FamilyNames.Values.Contains("Inter"));
        Assert.IsNotNull(inter, "no family named Inter among " + string.Join(", ", families.Select(x => x.Source)));

        var available = inter.GetTypefaces().Select(x => x.Weight).Distinct().ToList();
        foreach (var weight in Typo.ShippedWeights)
            Assert.Contains(weight, available, $"{weight} is missing from the embedded font");
    }

    [TestMethod]
    public void TheFontCarriesItsLicence()
        => Assert.IsTrue(File.Exists(Path.Combine(FontFolder(), "LICENSE.txt")));
}
