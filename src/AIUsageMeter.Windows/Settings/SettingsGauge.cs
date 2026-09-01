using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;
using AIUsageMeter.Windows.Overlay;

namespace AIUsageMeter.Windows.Settings;

/// <summary>The same ring-and-glyph as the rail, sized for a settings row or a pane header.</summary>
internal sealed class SettingsGauge : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(SettingsGauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(ProviderStatus), typeof(SettingsGauge),
        new FrameworkPropertyMetadata(ProviderStatus.Ready, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProviderProperty = DependencyProperty.Register(
        nameof(Provider), typeof(ProviderId), typeof(SettingsGauge),
        new FrameworkPropertyMetadata(ProviderId.Claude, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ExtentProperty = DependencyProperty.Register(
        nameof(Extent), typeof(double), typeof(SettingsGauge),
        new FrameworkPropertyMetadata(18d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public ProviderStatus Status { get => (ProviderStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public ProviderId Provider { get => (ProviderId)GetValue(ProviderProperty); set => SetValue(ProviderProperty, value); }
    public double Extent { get => (double)GetValue(ExtentProperty); set => SetValue(ExtentProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Extent, Extent);

    protected override void OnRender(DrawingContext context)
    {
        var extent = Extent;
        var ring = Math.Max(2, extent * 0.11);
        var radius = (extent - ring) / 2;
        if (radius <= 0) return;
        var centre = new Point(extent / 2, extent / 2);
        context.DrawEllipse(null, Pen(Palette.RingTrack, ring, rounded: false), centre, radius, radius);

        var fraction = Math.Clamp(Percent, 0, 100) / 100;
        if (fraction > 0)
            context.DrawGeometry(null, Pen(Palette.Usage(Percent, Status), ring), RingGeometry.Arc(centre, radius, fraction));

        var glyph = extent * 0.42;
        GlyphRenderer.Draw(context, ProviderGlyphs.For(Provider),
            new Rect(centre.X - glyph / 2, centre.Y - glyph / 2, glyph, glyph),
            Palette.Primary, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private static Pen Pen(Brush brush, double thickness, bool rounded = true)
    {
        var pen = new Pen(brush, thickness);
        if (rounded)
        {
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
        }
        pen.Freeze();
        return pen;
    }
}

internal sealed class EnumEqualsConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return System.Windows.Data.Binding.DoNothing;
        return Enum.Parse(targetType, parameter.ToString()!);
    }
}
