using AIUsageMeter.Core;

namespace AIUsageMeter.Windows.Design;

/// <summary>
/// The overlay's colours, mirroring <c>Palette</c> in <c>Sources/AIUsageMeter/Design.swift</c>.
/// </summary>
/// <remarks>
/// Every brush is frozen, so the render thread can take it without a copy and the whole table can be
/// shared. The view models previously built a <c>BrushConverter</c> and parsed a hex string per
/// provider per refresh, and left the results unfrozen.
/// </remarks>
internal static class Palette
{
    public static SolidColorBrush Surface { get; } = Frozen(Colors.Black);
    public static SolidColorBrush Edge { get; } = Frozen(Colors.White, 0.13);
    public static SolidColorBrush RingTrack { get; } = Frozen(Colors.White, 0.21);
    public static SolidColorBrush BarTrack { get; } = Frozen(Colors.White, 0.19);
    public static SolidColorBrush Primary { get; } = Frozen(Colors.White);

    /// <summary>The rail caption when its card is not open: macOS dims it to 0.86.</summary>
    public static SolidColorBrush PrimaryMuted { get; } = Frozen(Colors.White, 0.86);

    /// <summary>The card footer's source line, dimmed to 0.82.</summary>
    public static SolidColorBrush PrimarySoft { get; } = Frozen(Colors.White, 0.82);
    public static SolidColorBrush Secondary { get; } = Frozen(Colors.White, 0.55);
    public static SolidColorBrush Tertiary { get; } = Frozen(Colors.White, 0.38);
    public static SolidColorBrush Divider { get; } = Frozen(Colors.White, 0.10);
    public static SolidColorBrush Dormant { get; } = Frozen(Colors.White, 0.30);
    public static SolidColorBrush ActiveFill { get; } = Frozen(Colors.White, 0.11);
    public static SolidColorBrush Heart { get; } = Frozen(Colors.White, 0.34);
    public static SolidColorBrush HeartActive { get; } = Frozen(Color.FromRgb(0xFF, 0x6F, 0x80));
    public static SolidColorBrush Sponsor { get; } = Frozen(Color.FromRgb(0xFF, 0xDD, 0x00));

    /// <summary>Grouped inset lists in Settings. White at 6% — quieter than <see cref="ActiveFill"/>.</summary>
    public static SolidColorBrush GroupFill { get; } = Frozen(Colors.White, 0.06);

    /// <summary>Fields sit inset in the group, slightly darker than <see cref="GroupFill"/>.</summary>
    public static SolidColorBrush Inset { get; } = Frozen(Color.FromRgb(0x08, 0x08, 0x09));

    /// <summary>A control in a group under the pointer. One step up from <see cref="ActiveFill"/>.</summary>
    public static SolidColorBrush HoverFill { get; } = Frozen(Colors.White, 0.16);

    /// <summary>The same control while it is held down.</summary>
    public static SolidColorBrush PressFill { get; } = Frozen(Colors.White, 0.22);

    /// <summary>
    /// The chosen segment of a segmented control. It sits on an inset track, so the chosen one is
    /// the lighter of the two: a darker chip would read as a hole cut in the card.
    /// </summary>
    public static SolidColorBrush ChipFill { get; } = Frozen(Colors.White, 0.18);

    /// <summary>The healthy-usage green, used only for an on-toggle.</summary>
    public static SolidColorBrush ToggleOn { get; } = Frozen((Color)ColorConverter.ConvertFromString(UsageColor.For(0))!);

    /// <summary>Endpoint and limit warnings in Settings, same amber as a 70–90% gauge.</summary>
    public static SolidColorBrush Warning { get; } = Frozen((Color)ColorConverter.ConvertFromString(UsageColor.For(80))!);

    /// <summary>Persist and Credential Manager failures. Overlay's high-usage red, not a raw alert.</summary>
    public static SolidColorBrush Failure { get; } = Frozen((Color)ColorConverter.ConvertFromString(UsageColor.For(95))!);

    /// <summary>Keyboard focus on carbon. White at 45%.</summary>
    public static SolidColorBrush FocusRing { get; } = Frozen(Colors.White, 0.45);

    /// <summary>
    /// One frozen brush per threshold, keyed by the hex <see cref="UsageColor"/> hands back, so a
    /// refresh across every provider reuses brushes instead of parsing strings.
    /// </summary>
    private static readonly Dictionary<string, SolidColorBrush> UsageBrushes =
        new[] { 0d, 50d, 70d, 90d }
            .Select(UsageColor.For)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(hex => hex, hex => Frozen((Color)ColorConverter.ConvertFromString(hex)!), StringComparer.Ordinal);

    /// <summary>The gauge tint for a reading, or <see cref="Dormant"/> when the provider is not ready.</summary>
    public static SolidColorBrush Usage(double percent, ProviderStatus status)
        => status == ProviderStatus.Ready ? Usage(percent) : Dormant;

    /// <summary>The threshold tint for a reading, ignoring provider status.</summary>
    public static SolidColorBrush Usage(double percent) => UsageBrushes[UsageColor.For(percent)];

    private static SolidColorBrush Frozen(Color color, double opacity = 1)
    {
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }
}
