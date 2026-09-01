using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Overlay;

/// <summary>
/// One provider's gauge: a track, the reading, a sweep while refreshing, and the provider's mark.
/// </summary>
/// <remarks>
/// Drawn rather than composed from shapes. A rail can hold twenty-seven of these, and the arc has to
/// be a real trimmed arc — see <see cref="RingGeometry"/> for what the previous fake cost.
/// </remarks>
internal sealed class UsageRing : FrameworkElement
{
    /// <summary>How much of the circle the refresh sweep covers.</summary>
    private const double SweepFraction = 0.25;

    /// <summary>How far the reading fades behind a running sweep, from macOS.</summary>
    private const double RefreshingOpacity = 0.42;
    private const double RefreshingOpacityReduced = 0.55;

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(UsageRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint), typeof(Brush), typeof(UsageRing),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RefreshingProperty = DependencyProperty.Register(
        nameof(Refreshing), typeof(bool), typeof(UsageRing),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Turned by an animation while <see cref="Refreshing"/>, in degrees.</summary>
    public static readonly DependencyProperty SweepAngleProperty = DependencyProperty.Register(
        nameof(SweepAngle), typeof(double), typeof(UsageRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public Brush Tint { get => (Brush)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public bool Refreshing { get => (bool)GetValue(RefreshingProperty); set => SetValue(RefreshingProperty, value); }
    public double SweepAngle { get => (double)GetValue(SweepAngleProperty); set => SetValue(SweepAngleProperty, value); }

    public required Metrics Metrics { get; init; }
    public required ProviderGlyph Glyph { get; init; }

    /// <summary>Set when the machine asks for less animation, matching the macOS reduced-motion path.</summary>
    public bool Reduced { get; init; }

    /// <summary>
    /// The radius the ring's path follows. The stroke is centred on it, so half a stroke width sits
    /// outside — which is exactly what makes the ring touch the edge of its box, as on macOS.
    /// </summary>
    public static double RadiusFor(double gauge, double ring) => (gauge - ring) / 2;

    protected override Size MeasureOverride(Size availableSize) => new(Metrics.Gauge, Metrics.Gauge);

    protected override void OnRender(DrawingContext context)
    {
        var extent = Math.Min(ActualWidth, ActualHeight);
        var radius = RadiusFor(extent, Metrics.GaugeRing);
        if (radius <= 0) return;

        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        context.DrawEllipse(null, StrokePen(Palette.RingTrack, Metrics.GaugeRing, rounded: false), centre, radius, radius);

        var fraction = Math.Clamp(Percent, 0, 100) / 100;
        if (fraction > 0)
        {
            var faded = Refreshing;
            if (faded) context.PushOpacity(Reduced ? RefreshingOpacityReduced : RefreshingOpacity);
            context.DrawGeometry(null, StrokePen(Tint, Metrics.GaugeRing), RingGeometry.Arc(centre, radius, fraction));
            if (faded) context.Pop();
        }

        if (Refreshing)
        {
            context.PushTransform(new RotateTransform(SweepAngle, centre.X, centre.Y));
            context.DrawGeometry(null, StrokePen(Palette.Primary, Metrics.GaugeRing),
                RingGeometry.Arc(centre, radius, SweepFraction));
            context.Pop();
        }

        var glyph = Metrics.Glyph;
        GlyphRenderer.Draw(context, Glyph,
            new Rect(centre.X - glyph / 2, centre.Y - glyph / 2, glyph, glyph),
            Palette.Primary, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private static Pen StrokePen(Brush brush, double thickness, bool rounded = true)
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

/// <summary>Draws a provider mark into a box, whatever kind of mark it is.</summary>
internal static class GlyphRenderer
{
    /// <summary>Segoe Fluent Icons on Windows 11, falling back to the Windows 10 icon font.</summary>
    public static FontFamily IconFont { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static void Draw(DrawingContext context, ProviderGlyph glyph, Rect box, Brush brush, double pixelsPerDip)
    {
        switch (glyph)
        {
            case ProviderGlyph.Vector vector:
                DrawVector(context, vector, box, brush);
                break;

            // macOS sets a symbol at 0.86 of the box, semibold.
            case ProviderGlyph.Icon icon:
                DrawText(context, char.ConvertFromUtf32(icon.Codepoint), IconFont,
                    box.Height * 0.86, FontWeights.SemiBold, box, brush, pixelsPerDip);
                break;

            // And a monogram at 0.82, or 0.52 once it runs to more than one letter.
            case ProviderGlyph.Letters letters:
                DrawText(context, letters.Text, Typo.Family,
                    box.Height * (letters.Text.Length > 1 ? 0.52 : 0.82), FontWeights.Bold, box, brush, pixelsPerDip);
                break;
        }
    }

    private static void DrawVector(DrawingContext context, ProviderGlyph.Vector vector, Rect box, Brush brush)
    {
        // The marks are built in a one-by-one box so one frozen geometry serves every size.
        context.PushTransform(new TranslateTransform(box.X, box.Y));
        context.PushTransform(new ScaleTransform(box.Width, box.Height));

        foreach (var layer in vector.Layers)
        {
            var faded = layer.Opacity < 1;
            if (faded) context.PushOpacity(layer.Opacity);

            if (layer.StrokeWidth > 0)
            {
                var pen = new Pen(brush, layer.StrokeWidth)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                pen.Freeze();
                context.DrawGeometry(null, pen, layer.Path);
            }
            else
            {
                context.DrawGeometry(brush, null, layer.Path);
            }

            if (faded) context.Pop();
        }

        context.Pop();
        context.Pop();
    }

    private static void DrawText(DrawingContext context, string text, FontFamily family, double size,
        FontWeight weight, Rect box, Brush brush, double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, pixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = box.Width
        };

        context.DrawText(formatted, new Point(box.X, box.Y + (box.Height - formatted.Height) / 2));
    }
}
