using System.Windows;
using System.Windows.Controls;
using AIUsageMeter.Core;

namespace AIUsageMeter.Windows.Design;

/// <summary>One entry in the type ramp.</summary>
/// <param name="Size">In DIPs. macOS points are used one for one, as every other metric is.</param>
/// <param name="Weight">Only weights the embedded font actually ships.</param>
/// <param name="TabularDigits">
/// Set where a number changes in place. Without it the glyphs are proportional and the readout
/// jerks sideways every refresh.
/// </param>
internal readonly record struct TextStyle(double Size, FontWeight Weight, bool TabularDigits = false);

/// <summary>
/// The type ramp, mirroring <c>Typo</c> in <c>Sources/AIUsageMeter/Design.swift</c>.
/// </summary>
/// <remarks>
/// <para>
/// Set in Inter, which is the closest freely redistributable match to SF Pro. SF Pro itself may not
/// be shipped, and Segoe UI Variable is noticeably wider and rounder at the 10 to 16 point sizes
/// this interface lives at.
/// </para>
/// <para>
/// The four static instances are embedded rather than <c>InterVariable.ttf</c>: WPF cannot render
/// OpenType variable fonts and would silently use the default instance for every weight.
/// </para>
/// <para>
/// Unlike <see cref="Metrics"/>, macOS does not round type sizes after scaling, so neither does this.
/// </para>
/// </remarks>
internal sealed class Typo
{
    /// <summary>Weights backed by an embedded file. Anything else would be synthesised.</summary>
    public static readonly FontWeight[] ShippedWeights =
        [FontWeights.Regular, FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold];

    private static readonly Typo SmallSize = new(OverlaySize.Small.Scale());
    private static readonly Typo MediumSize = new(OverlaySize.Medium.Scale());
    private static readonly Typo LargeSize = new(OverlaySize.Large.Scale());

    private static readonly Lazy<FontFamily> InterFamily = new(() =>
    {
        // Registers the "pack" scheme. Without it the Uri below will not even parse: the commas in
        // "application:,,," are read as a port. WPF registers it from Application's static
        // constructor, which has not necessarily run this early, or in a host with no Application.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        return new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Inter");
    });

    private Typo(double scale)
    {
        Scale = scale;

        GaugeValue = new(16 * scale, FontWeights.SemiBold, TabularDigits: true);
        CardTitle = new(16 * scale, FontWeights.Bold);
        HeaderMeta = new(10 * scale, FontWeights.Regular);
        RowLabel = new(12.5 * scale, FontWeights.SemiBold);
        RowMeta = new(10.5 * scale, FontWeights.Regular);
        RowValue = new(13 * scale, FontWeights.Bold, TabularDigits: true);
        StateTitle = new(12.5 * scale, FontWeights.SemiBold);
        StateBody = new(11.5 * scale, FontWeights.Regular);
        FooterPrimary = new(11.5 * scale, FontWeights.SemiBold);
        FooterSecondary = new(10.5 * scale, FontWeights.Regular);
        Action = new(10.5 * scale, FontWeights.Medium);
        ActionGlyph = new(7.5 * scale, FontWeights.Bold);
        Pin = new(8.5 * scale, FontWeights.SemiBold);
        Setup = new(11 * scale, FontWeights.Medium);
        Support = new(11 * scale, FontWeights.SemiBold);
        SetupGlyph = new(15 * scale, FontWeights.SemiBold);
    }

    public static Typo For(OverlaySize size) => size switch
    {
        OverlaySize.Small => SmallSize,
        OverlaySize.Large => LargeSize,
        _ => MediumSize
    };

    /// <summary>The embedded Inter family, for anything that sets a font directly.</summary>
    public static FontFamily Family => InterFamily.Value;

    public double Scale { get; }

    public TextStyle GaugeValue { get; }
    public TextStyle CardTitle { get; }
    public TextStyle HeaderMeta { get; }
    public TextStyle RowLabel { get; }
    public TextStyle RowMeta { get; }
    public TextStyle RowValue { get; }
    public TextStyle StateTitle { get; }
    public TextStyle StateBody { get; }
    public TextStyle FooterPrimary { get; }
    public TextStyle FooterSecondary { get; }
    public TextStyle Action { get; }
    public TextStyle ActionGlyph { get; }
    public TextStyle Pin { get; }
    public TextStyle Setup { get; }
    public TextStyle Support { get; }
    public TextStyle SetupGlyph { get; }

    /// <summary>Every style in the ramp, for checks that must cover all of them.</summary>
    public IEnumerable<TextStyle> All =>
    [
        GaugeValue, CardTitle, HeaderMeta, RowLabel, RowMeta, RowValue, StateTitle, StateBody,
        FooterPrimary, FooterSecondary, Action, ActionGlyph, Pin, Setup, Support, SetupGlyph
    ];
}

internal static class TextStyleExtensions
{
    /// <summary>
    /// Applies a style to a text block, including greyscale antialiasing. macOS has drawn text
    /// without subpixel antialiasing since Mojave, and ClearType's colour fringing gives the
    /// difference away instantly at these sizes.
    /// </summary>
    public static T Apply<T>(this T element, TextStyle style) where T : TextBlock
    {
        element.FontFamily = Typo.Family;
        element.FontSize = style.Size;
        element.FontWeight = style.Weight;
        System.Windows.Media.TextOptions.SetTextRenderingMode(element, System.Windows.Media.TextRenderingMode.Grayscale);
        System.Windows.Media.TextOptions.SetTextFormattingMode(element, System.Windows.Media.TextFormattingMode.Ideal);
        if (style.TabularDigits)
            System.Windows.Documents.Typography.SetNumeralAlignment(element, FontNumeralAlignment.Tabular);
        return element;
    }
}
