using System.Windows;

namespace AIUsageMeter.Windows.Design;

/// <summary>
/// Layout for the Settings window's detail pane, the way macOS System Settings measures one: a
/// centred reading column, grouped cards of equal-height rows, controls on a single trailing edge,
/// and a footnote under the group it explains.
/// </summary>
/// <remarks>
/// These are unscaled. The overlay scales with <see cref="Metrics"/> because it floats over another
/// app's content; Settings is an ordinary desktop window and keeps one size.
/// <para>
/// Held here rather than typed into the XAML so a row's height, a card's radius and a field's width
/// are stated once. XAML reaches a <see cref="Thickness"/> or a <see cref="CornerRadius"/> through
/// <c>x:Static</c> only when it is already that type, so the composed values are given as well.
/// </para>
/// </remarks>
internal static class SettingsMetrics
{
    /// <summary>The reading column. Wider than this and a row's label drifts away from its control.</summary>
    public const double ColumnWidth = 620;

    /// <summary>The gutter either side of the column, and the pane's top and bottom air.</summary>
    public const double PaneInset = 32;

    public const double PaneTop = 30;
    public const double PaneBottom = 44;

    /// <summary>One row: label, control, and the same height whichever control it holds.</summary>
    public const double RowHeight = 44;

    /// <summary>Leading and trailing inset inside a card. The hairline starts at the same place.</summary>
    public const double RowInset = 16;

    /// <summary>Air above and below a row that wraps to more than one line.</summary>
    public const double RowPad = 11;

    public const double CardCorner = 12;

    /// <summary>Every short text field is this wide, so a card's fields share both edges.</summary>
    public const double FieldWidth = 260;

    public const double FieldHeight = 30;
    public const double FieldCorner = 7;

    /// <summary>The gauge beside a provider's name at the top of its pane.</summary>
    public const double HeaderGauge = 46;

    /// <summary>
    /// Leading for wrapped prose. WPF's default is the font's own, which sets Inter at 13 far too
    /// tight for a paragraph and leaves a footnote looking like a block of grey.
    /// </summary>
    public const double BodyLine = 19;

    public const double FootnoteLine = 17;

    public static Thickness Pane { get; } = new(PaneInset, PaneTop, PaneInset, PaneBottom);

    /// <summary>A row's own inset. Cards carry no padding: the rows and the hairline place themselves.</summary>
    public static Thickness Row { get; } = new(RowInset, 0, RowInset, 0);

    /// <summary>A row whose content wraps, so it grows downwards instead of centring in 44.</summary>
    public static Thickness RowStacked { get; } = new(RowInset, RowPad, RowInset, RowPad);

    /// <summary>Separators are inset at the leading edge, as a grouped list's are.</summary>
    public static Thickness Separator { get; } = new(RowInset, 0, 0, 0);

    /// <summary>A section header, with the air above it that separates one group from the last.</summary>
    public static Thickness Section { get; } = new(RowInset, 26, RowInset, 8);

    /// <summary>A footnote sits under its card, aligned with the label above it.</summary>
    public static Thickness Footnote { get; } = new(RowInset, 8, RowInset, 0);

    public static CornerRadius Card { get; } = new(CardCorner);
    public static CornerRadius Field { get; } = new(FieldCorner);

    /// <summary>
    /// The keyboard ring is drawn outside the control it marks, on a negative margin, so gaining
    /// focus never resizes the control's own content box. Its radius follows the offset.
    /// </summary>
    public const double RingOffset = 3;

    public static Thickness Ring { get; } = new(-RingOffset);
    public static CornerRadius FieldRing { get; } = new(FieldCorner + RingOffset);
}
