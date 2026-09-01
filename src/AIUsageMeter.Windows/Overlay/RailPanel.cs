using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Overlay;

/// <summary>
/// The rail: a black column pinned to the trailing edge, holding one gauge per provider with the
/// support heart beneath a hairline.
/// </summary>
/// <remarks>
/// A port of <c>ProviderRail</c> in <c>Sources/AIUsageMeter/NotchViews.swift</c>. A panel rather than
/// a composed control so the rail's own background lands beneath its gauges, which is where the
/// element's <c>OnRender</c> output goes.
/// </remarks>
internal sealed class RailPanel : Panel
{
    private readonly Metrics _metrics;
    private readonly Typo _typo;
    private readonly bool _reduced;
    private readonly SupportHeart _heart;
    private readonly List<GaugeItem> _gauges = [];
    private SetupButton? _setup;

    public RailPanel(Metrics metrics, Typo typo, bool reduced = false)
    {
        _metrics = metrics;
        _typo = typo;
        _reduced = reduced;

        _heart = new SupportHeart(metrics.SupportButton, reduced);
        _heart.Activated += (_, _) => SupportRequested?.Invoke(this, EventArgs.Empty);
        Children.Add(_heart);

        Effect = RailShadow(metrics);
        SnapsToDevicePixels = true;
        UseLayoutRounding = false;   // the metrics are already whole numbers; rounding twice shifts them
    }

    public event EventHandler? SupportRequested;
    public event EventHandler? SetupRequested;
    public event EventHandler<ProviderId>? GaugeActivated;

    public IReadOnlyList<GaugeItem> Gauges => _gauges;
    public SupportHeart Heart => _heart;

    /// <summary>How far apart the gauges currently sit; it tightens as providers are added.</summary>
    public double Spacing => _metrics.ItemSpacing(Math.Max(1, _gauges.Count));

    /// <summary>The height the rail wants, before any clamping to the screen.</summary>
    public double NaturalHeight => _metrics.RailHeight(Math.Max(1, VisibleCount), Spacing);

    private int VisibleCount => _gauges.Count > 0 ? _gauges.Count : 1;

    /// <summary>
    /// Replaces the gauges. Existing rows are updated in place so a refresh does not rebuild the
    /// whole rail, which would drop the pointer out of whichever gauge it was over.
    /// </summary>
    public void SetProviders(IReadOnlyList<ProviderSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            foreach (var gauge in _gauges) Children.Remove(gauge);
            _gauges.Clear();

            if (_setup is null)
            {
                _setup = new SetupButton(_metrics, _typo);
                _setup.Activated += (_, _) => SetupRequested?.Invoke(this, EventArgs.Empty);
                Children.Insert(0, _setup);
            }

            InvalidateMeasure();
            return;
        }

        if (_setup is not null)
        {
            Children.Remove(_setup);
            _setup = null;
        }

        // Reuse a row when the provider in that slot has not changed.
        for (var index = 0; index < snapshots.Count; index++)
        {
            if (index < _gauges.Count && _gauges[index].Id == snapshots[index].Id)
            {
                _gauges[index].Update(snapshots[index]);
                continue;
            }

            var gauge = Build(snapshots[index]);
            if (index < _gauges.Count)
            {
                Children.Remove(_gauges[index]);
                _gauges[index] = gauge;
                Children.Insert(index, gauge);
            }
            else
            {
                _gauges.Add(gauge);
                Children.Insert(index, gauge);
            }
        }

        for (var index = _gauges.Count - 1; index >= snapshots.Count; index--)
        {
            Children.Remove(_gauges[index]);
            _gauges.RemoveAt(index);
        }

        InvalidateMeasure();
    }

    /// <summary>Marks one gauge as the one whose card is open, and clears the rest.</summary>
    public void SetActive(ProviderId? id)
    {
        foreach (var gauge in _gauges) gauge.Active = gauge.Id == id;
    }

    public void SetRefreshing(IReadOnlySet<ProviderId> refreshing)
    {
        foreach (var gauge in _gauges) gauge.Refreshing = refreshing.Contains(gauge.Id);
    }

    /// <summary>Where a gauge's centre sits, measured from the top of the rail.</summary>
    public double GaugeCentre(ProviderId id)
    {
        var index = _gauges.FindIndex(x => x.Id == id);
        if (index < 0) return NaturalHeight / 2;
        return _metrics.RailPadding + index * (_metrics.Item + Spacing) + _metrics.Gauge / 2;
    }

    private GaugeItem Build(ProviderSnapshot snapshot)
    {
        var gauge = new GaugeItem(snapshot, _metrics, _typo, _reduced);
        gauge.MouseLeftButtonUp += (_, _) => GaugeActivated?.Invoke(this, gauge.Id);
        gauge.Cursor = Cursors.Hand;
        return gauge;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var row = new Size(_metrics.RailWidth, _metrics.Item);
        foreach (var gauge in _gauges) gauge.Measure(row);
        _setup?.Measure(row);
        _heart.Measure(new Size(_metrics.RailWidth, _metrics.SupportButton));
        return new Size(_metrics.RailWidth, NaturalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var spacing = Spacing;
        var y = _metrics.RailPadding;

        if (_setup is not null)
        {
            _setup.Arrange(new Rect(0, y, finalSize.Width, _metrics.Item));
            y += _metrics.Item;
        }

        foreach (var gauge in _gauges)
        {
            gauge.Arrange(new Rect(0, y, finalSize.Width, _metrics.Item));
            y += _metrics.Item + spacing;
        }

        // The heart hangs off the bottom of the rail, not off the last gauge.
        var heartTop = finalSize.Height - _metrics.RailPadding - _metrics.SupportButton;
        _heart.Arrange(new Rect(0, heartTop, finalSize.Width, _metrics.SupportButton));
        return finalSize;
    }

    protected override void OnRender(DrawingContext context)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // Continuous corners on the leading edge only; the trailing edge runs off the screen.
        var shape = Squircle.RoundedRect(bounds, CornerRadii.Left(_metrics.RailCorner));
        context.DrawGeometry(Palette.Surface, null, shape);

        // The border is drawn on a shape that overhangs to the right, so the stroke never appears
        // down the trailing edge, which sits past the edge of the display.
        var hairline = DevicePixels.Snap(Metrics.Hairline, VisualTreeHelper.GetDpi(this).DpiScaleX);
        var overhang = new Rect(bounds.X, bounds.Y, bounds.Width + hairline * 2, bounds.Height);
        var pen = new Pen(Palette.Edge, hairline);
        pen.Freeze();
        context.DrawGeometry(null, pen, Squircle.RoundedRect(overhang, CornerRadii.Left(_metrics.RailCorner)));

        // A hairline above the heart, 44 per cent of the rail's width, centred.
        var dividerWidth = _metrics.RailWidth * 0.44;
        var dividerY = RenderSize.Height - _metrics.RailPadding - _metrics.SupportButton - _metrics.SupportGap;
        context.DrawRectangle(Palette.Divider, null,
            new Rect((RenderSize.Width - dividerWidth) / 2, dividerY, dividerWidth, hairline));
    }

    /// <summary>
    /// macOS asks for black at 34 per cent, radius 13.2, offset (-4, 5).
    /// </summary>
    /// <remarks>
    /// SwiftUI's shadow radius and WPF's <c>BlurRadius</c> are not the same quantity, and WPF states
    /// its offset in polar form. The direction and depth below are exact; the blur is doubled as a
    /// starting point and is the one number here still waiting on the snapshot diff to settle it.
    /// </remarks>
    private static DropShadowEffect RailShadow(Metrics metrics)
    {
        var effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.34,
            BlurRadius = metrics.ShadowSlack * 0.6 * 2,
            ShadowDepth = Math.Sqrt(4 * 4 + 5 * 5),
            Direction = 231.34,
            RenderingBias = RenderingBias.Quality
        };
        effect.Freeze();
        return effect;
    }
}
