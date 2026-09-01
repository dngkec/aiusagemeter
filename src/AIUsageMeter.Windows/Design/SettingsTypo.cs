using System.Windows;

namespace AIUsageMeter.Windows.Design;

/// <summary>
/// Unscaled type for the Settings window. Overlay type follows <see cref="Typo"/> and scales with
/// overlay size; Settings is a desktop window and stays at these sizes.
/// </summary>
internal static class SettingsTypo
{
    public static TextStyle PaneTitle { get; } = new(20, FontWeights.Bold);
    public static TextStyle RowLabel { get; } = new(13, FontWeights.Medium);
    public static TextStyle Meta { get; } = new(12, FontWeights.Regular, TabularDigits: true);
    public static TextStyle Footer { get; } = new(12, FontWeights.Regular);
    public static TextStyle Sidebar { get; } = new(13, FontWeights.Medium);
    public static TextStyle SidebarSection { get; } = new(11, FontWeights.SemiBold);
    public static TextStyle Search { get; } = new(13, FontWeights.Regular);
    public static TextStyle Support { get; } = new(13, FontWeights.SemiBold);

    /// <summary>The header over a group of rows. One step above the footnote under it.</summary>
    public static TextStyle Section { get; } = new(13, FontWeights.SemiBold);

    /// <summary>Prose inside a pane: a provider's summary, a status message, a field's value.</summary>
    public static TextStyle Body { get; } = new(13, FontWeights.Regular);

    public static IEnumerable<TextStyle> All =>
        [PaneTitle, RowLabel, Meta, Footer, Sidebar, SidebarSection, Search, Support, Section, Body];

    // XAML reaches the ramp through x:Static, which binds to a member and not to a record field.
    // Without these the window would hard-code its own sizes and the ramp would drift out of use.
    public static double PaneTitleSize => PaneTitle.Size;
    public static FontWeight PaneTitleWeight => PaneTitle.Weight;
    public static double RowLabelSize => RowLabel.Size;
    public static FontWeight RowLabelWeight => RowLabel.Weight;
    public static double MetaSize => Meta.Size;
    public static FontWeight MetaWeight => Meta.Weight;
    public static double FooterSize => Footer.Size;
    public static double SidebarSize => Sidebar.Size;
    public static FontWeight SidebarWeight => Sidebar.Weight;
    public static double SidebarSectionSize => SidebarSection.Size;
    public static FontWeight SidebarSectionWeight => SidebarSection.Weight;
    public static double SearchSize => Search.Size;
    public static double SupportSize => Support.Size;
    public static FontWeight SupportWeight => Support.Weight;
    public static double SectionSize => Section.Size;
    public static FontWeight SectionWeight => Section.Weight;
    public static double BodySize => Body.Size;
    public static FontWeight BodyWeight => Body.Weight;
}
