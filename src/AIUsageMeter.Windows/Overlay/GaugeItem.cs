using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Overlay;

/// <summary>
/// One row of the rail: a gauge with its reading underneath.
/// </summary>
/// <remarks>
/// A port of <c>GaugeItem</c> in <c>Sources/AIUsageMeter/NotchViews.swift</c>. The ring is a real
/// child so it can animate on its own; the caption and the active disc are drawn here, and an
/// element's own drawing lands beneath its children, which is where the disc belongs.
/// </remarks>
internal sealed class GaugeItem : FrameworkElement
{
    private readonly Metrics _metrics;
    private readonly Typo _typo;
    private readonly UsageRing _ring;
    private ProviderSnapshot _snapshot;
    private bool _active;

    public GaugeItem(ProviderSnapshot snapshot, Metrics metrics, Typo typo, bool reduced = false)
    {
        _snapshot = snapshot;
        _metrics = metrics;
        _typo = typo;
        _ring = new UsageRing
        {
            Metrics = metrics,
            Glyph = ProviderGlyphs.For(snapshot.Id),
            Reduced = reduced,
            Percent = Reading(snapshot),
            Tint = Palette.Usage(Reading(snapshot), snapshot.Status)
        };

        _caption = new System.Windows.Controls.TextBlock
        {
            TextAlignment = TextAlignment.Center,
            Width = metrics.RailWidth,
            Foreground = CaptionTint(snapshot, active: false),
            Text = Caption(snapshot)
        }.Apply(typo.GaugeValue);

        AddVisualChild(_ring);
        AddLogicalChild(_ring);
        AddVisualChild(_caption);
        AddLogicalChild(_caption);
        Focusable = true;
        RenderTransformOrigin = new Point(0.5, 0.5);
    }

    private readonly System.Windows.Controls.TextBlock _caption;

    public ProviderId Id => _snapshot.Id;

    /// <summary>The gauge whose card is open. Grows slightly and gains a disc behind the ring.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            // 1.05 on macOS. Applied here rather than to the ring so the caption grows with it.
            RenderTransform = value ? new ScaleTransform(1.05, 1.05) : Transform.Identity;
            _caption.Foreground = CaptionTint(_snapshot, value);
            InvalidateVisual();
        }
    }

    public bool Pinned { get; set; }

    public bool Refreshing
    {
        get => _ring.Refreshing;
        set => _ring.Refreshing = value;
    }

    public UsageRing Ring => _ring;

    public void Update(ProviderSnapshot snapshot)
    {
        _snapshot = snapshot;
        _ring.Percent = Reading(snapshot);
        _ring.Tint = Palette.Usage(Reading(snapshot), snapshot.Status);
        _caption.Text = Caption(snapshot);
        _caption.Foreground = CaptionTint(snapshot, Active);
        InvalidateVisual();
    }

    /// <summary>The reading as a percentage, clamped the way macOS clamps it.</summary>
    public static double Reading(ProviderSnapshot snapshot) => Math.Clamp(snapshot.PrimaryPercent, 0, 100);

    /// <summary>What sits under the gauge: the reading, or a dash when there is nothing to show.</summary>
    public static string Caption(ProviderSnapshot snapshot)
        => snapshot.Status == ProviderStatus.Ready
            ? $"{Math.Round(Reading(snapshot), MidpointRounding.AwayFromZero):F0}%"
            : "—";

    public static Brush CaptionTint(ProviderSnapshot snapshot, bool active)
    {
        if (snapshot.Status != ProviderStatus.Ready) return Palette.Tertiary;
        return active ? Palette.Primary : Palette.PrimaryMuted;
    }

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _ring,
        1 => _caption,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    protected override Size MeasureOverride(Size availableSize)
    {
        _ring.Measure(new Size(_metrics.Gauge, _metrics.Gauge));
        _caption.Measure(new Size(_metrics.RailWidth, _metrics.GaugeLabel));
        return new Size(_metrics.RailWidth, _metrics.Item);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var left = (finalSize.Width - _metrics.Gauge) / 2;
        _ring.Arrange(new Rect(left, 0, _metrics.Gauge, _metrics.Gauge));

        var top = _metrics.Gauge + _metrics.GaugeLabelGap;
        var height = Math.Min(_caption.DesiredSize.Height, _metrics.GaugeLabel);
        _caption.Arrange(new Rect(0, top + (_metrics.GaugeLabel - height) / 2, finalSize.Width, height));
        return finalSize;
    }

    protected override void OnRender(DrawingContext context)
    {
        // A transparent ground so the whole row takes the pointer, not just the ink.
        context.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(RenderSize));

        var centre = new Point(RenderSize.Width / 2, _metrics.Gauge / 2);
        if (Active)
        {
            // macOS pads the disc outward by a ring width, so it reads as a halo, not a backing.
            var radius = _metrics.Gauge / 2 + _metrics.GaugeRing;
            context.DrawEllipse(Palette.ActiveFill, null, centre, radius, radius);
        }

    }
}

/// <summary>The heart under the rail, and in the card's footer.</summary>
internal sealed class SupportHeart : FrameworkElement
{
    private readonly double _extent;
    private bool _hovering;

    public SupportHeart(double extent, bool reduced = false)
    {
        _extent = extent;
        Reduced = reduced;
        Cursor = Cursors.Hand;
        RenderTransformOrigin = new Point(0.5, 0.5);
        ToolTip = "Support AIUsageMeter";
    }

    public bool Reduced { get; }

    public event EventHandler? Activated;

    public bool Hovering
    {
        get => _hovering;
        set
        {
            if (_hovering == value) return;
            _hovering = value;
            // 1.14 on macOS, skipped when the machine asks for less movement.
            RenderTransform = value && !Reduced ? new ScaleTransform(1.14, 1.14) : Transform.Identity;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(_extent, _extent);

    protected override void OnMouseEnter(MouseEventArgs e) { Hovering = true; base.OnMouseEnter(e); }
    protected override void OnMouseLeave(MouseEventArgs e) { Hovering = false; base.OnMouseLeave(e); }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        Activated?.Invoke(this, EventArgs.Empty);
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(RenderSize));

        var side = Math.Min(RenderSize.Width, RenderSize.Height);
        if (side <= 0) return;

        context.PushTransform(new TranslateTransform((RenderSize.Width - side) / 2, (RenderSize.Height - side) / 2));
        context.PushTransform(new ScaleTransform(side, side));

        if (Hovering)
        {
            context.DrawGeometry(Palette.HeartActive, null, Marks.Heart);
        }
        else
        {
            var pen = new Pen(Palette.Heart, 0.11) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            context.DrawGeometry(null, pen, Marks.Heart);
        }

        context.Pop();
        context.Pop();
    }
}

/// <summary>Shown in place of the gauges when no provider is turned on.</summary>
internal sealed class SetupButton : FrameworkElement
{
    private readonly Metrics _metrics;
    private readonly Typo _typo;

    public SetupButton(Metrics metrics, Typo typo)
    {
        _metrics = metrics;
        _typo = typo;
        Cursor = Cursors.Hand;
    }

    public event EventHandler? Activated;

    protected override Size MeasureOverride(Size availableSize) => new(_metrics.RailWidth, _metrics.Item);

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        Activated?.Invoke(this, EventArgs.Empty);
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(RenderSize));

        var centre = new Point(RenderSize.Width / 2, _metrics.Gauge / 2);
        var radius = UsageRing.RadiusFor(_metrics.Gauge, _metrics.GaugeRing);

        // Dashes of 3 on 7, in stroke widths, as macOS specifies them.
        var pen = new Pen(Palette.RingTrack, _metrics.GaugeRing)
        {
            DashStyle = new DashStyle([3, 7], 0),
            DashCap = PenLineCap.Flat
        };
        pen.Freeze();
        context.DrawEllipse(null, pen, centre, radius, radius);

        var glyph = _typo.SetupGlyph.Size;
        context.PushTransform(new TranslateTransform(centre.X - glyph / 2, centre.Y - glyph / 2));
        context.PushTransform(new ScaleTransform(glyph, glyph));
        context.DrawGeometry(Palette.Primary, null, Marks.Plus);
        context.Pop();
        context.Pop();

        var label = new FormattedText(
            "Set up", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Typo.Family, FontStyles.Normal, _typo.Setup.Weight, FontStretches.Normal),
            _typo.Setup.Size, Palette.Secondary, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = RenderSize.Width
        };

        var top = _metrics.Gauge + _metrics.GaugeLabelGap;
        context.DrawText(label, new Point(0, top + (_metrics.GaugeLabel - label.Height) / 2));
    }
}
