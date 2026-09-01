using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Overlay;

/// <summary>
/// What the overlay shrinks to once it has been left alone: a sliver at the edge of the screen.
/// </summary>
/// <remarks>
/// A port of <c>MiniTab</c>. The tab itself is 8 by 52; the element is wider and taller so there is
/// something to aim at, which is the whole reason the overlay can afford to get out of the way.
/// </remarks>
internal sealed class MiniTab : FrameworkElement
{
    private readonly Metrics _metrics;

    public MiniTab(Metrics metrics)
    {
        _metrics = metrics;
        Cursor = Cursors.Hand;
        Effect = TabShadow();
        ToolTip = "Show AIUsageMeter";
    }

    /// <summary>Raised when the pointer finds the tab, or clicks it.</summary>
    public event EventHandler? Revealed;

    /// <summary>The pointer target, which is larger than the ink.</summary>
    public Size Target => new(_metrics.MiniTarget, _metrics.MiniHeight + 26);

    protected override Size MeasureOverride(Size availableSize) => Target;

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        Revealed?.Invoke(this, EventArgs.Empty);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        Revealed?.Invoke(this, EventArgs.Empty);
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnRender(DrawingContext context)
    {
        // A transparent ground so the whole target takes the pointer, not just the sliver.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var tab = new Rect(
            RenderSize.Width - _metrics.MiniWidth,
            (RenderSize.Height - _metrics.MiniHeight) / 2,
            _metrics.MiniWidth,
            _metrics.MiniHeight);

        context.DrawGeometry(Palette.Surface, null,
            Squircle.RoundedRect(tab, CornerRadii.Left(_metrics.MiniWidth / 2)));
    }

    /// <summary>macOS asks for black at 25 per cent, radius 6, offset (-2, 2).</summary>
    private static DropShadowEffect TabShadow()
    {
        var effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.25,
            BlurRadius = 12,
            ShadowDepth = Math.Sqrt(8),
            Direction = 225,
            RenderingBias = RenderingBias.Quality
        };
        effect.Freeze();
        return effect;
    }
}
