using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Overlay;

/// <summary>
/// One usage window: its name, the reading, a bar, and when it next resets.
/// </summary>
/// <remarks>A port of <c>UsageRow</c>. Fixed height, so the card's total height is known up front.</remarks>
internal sealed class UsageRowControl : Panel
{
    private readonly Metrics _metrics;
    private readonly TextBlock _label;
    private readonly TextBlock _value;
    private readonly TextBlock _reset;
    private readonly UsageWindow _window;

    public UsageRowControl(UsageWindow window, Metrics metrics, Typo typo, DateTimeOffset now)
    {
        _metrics = metrics;
        _window = window;

        _label = Add(new TextBlock { Text = window.Label, Foreground = Palette.Primary, TextTrimming = TextTrimming.CharacterEllipsis }
            .Apply(typo.RowLabel));
        _value = Add(new TextBlock { Text = window.ReadingCaption, Foreground = Palette.Usage(window.Percent), TextAlignment = TextAlignment.Right }
            .Apply(typo.RowValue));
        _reset = Add(new TextBlock { Text = RelativeTime.ResetLine(window, now), Foreground = Palette.Secondary, TextTrimming = TextTrimming.CharacterEllipsis }
            .Apply(typo.RowMeta));
    }

    private T Add<T>(T child) where T : UIElement
    {
        Children.Add(child);
        return child;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? _metrics.CardWidth : availableSize.Width;
        _value.Measure(new Size(width, _metrics.RowLine));
        _label.Measure(new Size(Math.Max(0, width - _value.DesiredSize.Width - 8), _metrics.RowLine));
        _reset.Measure(new Size(width, _metrics.RowMeta));
        return new Size(width, _metrics.Card.Row);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var value = Math.Min(_value.DesiredSize.Width, finalSize.Width);
        _label.Arrange(new Rect(0, 0, Math.Max(0, finalSize.Width - value - 8), _metrics.RowLine));
        _value.Arrange(new Rect(finalSize.Width - value, 0, value, _metrics.RowLine));

        // 46 high holds a 16 line, a 5 bar and a 14 caption; the 11 left over splits evenly.
        var slack = (finalSize.Height - _metrics.RowLine - _metrics.BarHeight - _metrics.RowMeta) / 2;
        _reset.Arrange(new Rect(0, finalSize.Height - _metrics.RowMeta, finalSize.Width, _metrics.RowMeta));
        BarTop = _metrics.RowLine + slack;
        return finalSize;
    }

    /// <summary>Where the capsule sits, worked out during arrange and drawn in render.</summary>
    private double BarTop { get; set; }

    protected override void OnRender(DrawingContext context)
    {
        var height = _metrics.BarHeight;
        var radius = height / 2;
        var track = new Rect(0, BarTop, RenderSize.Width, height);
        context.DrawRoundedRectangle(Palette.BarTrack, null, track, radius, radius);

        var fraction = Math.Clamp(_window.Fraction, 0, 1);
        // Never narrower than the capsule is tall, so a small reading still reads as a bar.
        var filled = Math.Max(height, RenderSize.Width * fraction);
        if (fraction > 0)
            context.DrawRoundedRectangle(Palette.Usage(_window.Percent), null,
                new Rect(0, BarTop, filled, height), radius, radius);
    }
}

/// <summary>Shown in place of the rows when there is no reading to show.</summary>
internal sealed class CardStateBlock : Panel
{
    private readonly TextBlock _title;
    private readonly TextBlock _message;

    public CardStateBlock(ProviderSnapshot snapshot, Typo typo)
    {
        _title = new TextBlock { Text = snapshot.Status.ShortLabel(), Foreground = Palette.Primary }.Apply(typo.StateTitle);
        // macOS caps this at three lines. WPF has no MaxLines, so the line height is pinned and the
        // block is capped at three of them; anything past that is trimmed with an ellipsis.
        var lineHeight = Math.Ceiling(typo.StateBody.Size * 1.32);
        _message = new TextBlock
        {
            Text = snapshot.Message ?? "Usage is temporarily unavailable.",
            Foreground = Palette.Secondary,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = lineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            MaxHeight = lineHeight * 3,
            TextTrimming = TextTrimming.CharacterEllipsis
        }.Apply(typo.StateBody);

        Children.Add(_title);
        Children.Add(_message);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _title.Measure(availableSize);
        _message.Measure(availableSize);
        return new Size(availableSize.Width, _title.DesiredSize.Height + 7 + _message.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _title.Arrange(new Rect(0, 0, finalSize.Width, _title.DesiredSize.Height));
        var top = _title.DesiredSize.Height + 7;
        _message.Arrange(new Rect(0, top, finalSize.Width, Math.Max(0, finalSize.Height - top)));
        return finalSize;
    }
}

/// <summary>
/// The detail card: a rounded body with a tail pointing back at the gauge it belongs to.
/// </summary>
/// <remarks>A port of <c>DetailCard</c> and <c>CardContent</c>.</remarks>
internal sealed class DetailCard : Panel
{
    private readonly Metrics _metrics;
    private readonly Typo _typo;
    private readonly ProviderSnapshot _snapshot;
    private readonly DateTimeOffset _now;

    private readonly GlyphMark _mark;
    private readonly TextBlock _title;
    private readonly TextBlock _updated;
    private readonly TextBlock _source;
    private readonly TextBlock _status;
    private readonly TextBlock _subtitle;
    private readonly SupportHeart _heart;
    private readonly List<UsageRowControl> _rows = [];
    private readonly CardStateBlock? _state;
    private readonly CardAction? _action;
    private readonly PinMark _pin;
    private double _tailCentre;

    public DetailCard(ProviderSnapshot snapshot, Metrics metrics, Typo typo, DateTimeOffset now, bool reduced = false)
    {
        _snapshot = snapshot;
        _metrics = metrics;
        _typo = typo;
        _now = now;

        _mark = Add(new GlyphMark(ProviderGlyphs.For(snapshot.Id), metrics.Glyph * 0.9));
        _title = Add(new TextBlock { Text = snapshot.Name, Foreground = Palette.Primary, TextTrimming = TextTrimming.CharacterEllipsis }
            .Apply(typo.CardTitle));
        _updated = Add(new TextBlock { Text = RelativeTime.Short(snapshot.Timestamp, now), Foreground = Palette.Tertiary, TextAlignment = TextAlignment.Right }
            .Apply(typo.HeaderMeta));
        _pin = Add(new PinMark(typo.Pin.Size) { Visibility = Visibility.Collapsed });

        if (snapshot.Status == ProviderStatus.Ready && snapshot.Windows.Count > 0)
        {
            foreach (var window in snapshot.FeaturedWindows(CardMetrics.MaximumRows))
                _rows.Add(Add(new UsageRowControl(window, metrics, typo, now)));
        }
        else
        {
            _state = Add(new CardStateBlock(snapshot, typo));
        }

        _source = Add(new TextBlock { Text = SourceLabel(snapshot), Foreground = Palette.PrimarySoft }.Apply(typo.FooterPrimary));
        _status = Add(new TextBlock { Text = snapshot.Status.ShortLabel(), Foreground = snapshot.Status.Tint(), TextAlignment = TextAlignment.Right }
            .Apply(typo.FooterPrimary));
        _subtitle = Add(new TextBlock { Text = Subtitle(snapshot, now), Foreground = Palette.Tertiary, TextTrimming = TextTrimming.CharacterEllipsis }
            .Apply(typo.FooterSecondary));

        _heart = Add(new SupportHeart(metrics.RowMeta, reduced));
        _heart.Activated += (_, _) => SupportRequested?.Invoke(this, EventArgs.Empty);

        if (snapshot.DashboardUrl is not null)
        {
            _action = Add(new CardAction("Dashboard", typo));
            _action.Activated += (_, _) => DashboardRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (snapshot.Status.NeedsAttention())
        {
            _action = Add(new CardAction("Settings", typo));
            _action.Activated += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        Effect = CardShadow(metrics);
    }

    public event EventHandler? SupportRequested;
    public event EventHandler? DashboardRequested;
    public event EventHandler? SettingsRequested;

    public ProviderId Id => _snapshot.Id;

    /// <summary>Kept open by a click. Shows a pin in the header, as macOS does.</summary>
    public bool Pinned
    {
        get => _pin.Visibility == Visibility.Visible;
        set
        {
            var wanted = value ? Visibility.Visible : Visibility.Collapsed;
            if (_pin.Visibility == wanted) return;
            _pin.Visibility = wanted;
            InvalidateArrange();
        }
    }

    /// <summary>Where the tail should point, measured down from the top of the card.</summary>
    public double TailCentre
    {
        get => _tailCentre;
        set
        {
            if (Math.Abs(_tailCentre - value) < 0.01) return;
            _tailCentre = value;
            InvalidateVisual();
        }
    }

    /// <summary>The card's height, from the shared metrics so the layout can place it before it exists.</summary>
    public double NaturalHeight => _metrics.Card.Height(_snapshot);

    /// <summary>Body plus tail. The card sits in a box this wide.</summary>
    public double NaturalWidth => _metrics.CardWidth + _metrics.TailWidth;

    private T Add<T>(T child) where T : UIElement
    {
        Children.Add(child);
        return child;
    }

    private static string SourceLabel(ProviderSnapshot snapshot)
        => snapshot.Source == DataSourceKind.Demo ? "Demo data" : snapshot.Source.ToString();

    private static string Subtitle(ProviderSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Source == DataSourceKind.Demo) return "Deterministic sample data";
        return snapshot.Windows.Count > CardMetrics.MaximumRows
            ? $"{snapshot.Windows.Count} usage windows"
            : $"Updated {RelativeTime.Clock(snapshot.Timestamp)}";
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = _metrics.CardWidth - _metrics.CardPaddingH * 2;
        var card = _metrics.Card;

        _mark.Measure(new Size(_metrics.Glyph * 0.9, card.Header));
        _updated.Measure(new Size(content, card.Header));
        _pin.Measure(new Size(content, card.Header));
        _title.Measure(new Size(Math.Max(0, content - _mark.DesiredSize.Width - _updated.DesiredSize.Width - 18), card.Header));

        foreach (var row in _rows) row.Measure(new Size(content, card.Row));
        _state?.Measure(new Size(content, card.State));

        _status.Measure(new Size(content, card.Footer));
        _source.Measure(new Size(content, card.Footer));
        _heart.Measure(new Size(_metrics.RowLine, _metrics.RowMeta));
        _action?.Measure(new Size(content, _metrics.RowMeta));
        _subtitle.Measure(new Size(content, _metrics.RowMeta));

        return new Size(NaturalWidth, NaturalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var card = _metrics.Card;
        var left = _metrics.CardPaddingH;
        var right = _metrics.CardWidth - _metrics.CardPaddingH;
        var content = right - left;
        var y = _metrics.CardPaddingV;

        // Header: mark, name, then how long ago the reading was taken, pinned to the trailing edge.
        var markSize = _metrics.Glyph * 0.9;
        _mark.Arrange(new Rect(left, y + (card.Header - markSize) / 2, markSize, markSize));

        var updatedWidth = Math.Min(_updated.DesiredSize.Width, content);
        _updated.Arrange(new Rect(right - updatedWidth, y + (card.Header - _updated.DesiredSize.Height) / 2,
            updatedWidth, _updated.DesiredSize.Height));

        var pinWidth = _pin.Visibility == Visibility.Visible ? _pin.DesiredSize.Width + 6 : 0;
        if (pinWidth > 0)
            _pin.Arrange(new Rect(right - updatedWidth - pinWidth, y + (card.Header - _pin.DesiredSize.Height) / 2,
                _pin.DesiredSize.Width, _pin.DesiredSize.Height));

        var titleLeft = left + markSize + 9;
        var titleWidth = Math.Max(0, right - updatedWidth - pinWidth - 6 - titleLeft);
        _title.Arrange(new Rect(titleLeft, y + (card.Header - _title.DesiredSize.Height) / 2,
            titleWidth, _title.DesiredSize.Height));

        y += card.Header + card.HeaderGap;

        if (_state is not null)
        {
            _state.Arrange(new Rect(left, y, content, card.State));
            y += card.State;
        }
        else
        {
            foreach (var row in _rows)
            {
                row.Arrange(new Rect(left, y, content, card.Row));
                y += card.Row + card.RowSpacing;
            }

            if (_rows.Count > 0) y -= card.RowSpacing;
        }

        // Footer: divider, then two lines.
        DividerTop = y + card.FooterLead;
        var footerTop = DividerTop + Metrics.Hairline + card.FooterTrail;

        var statusWidth = Math.Min(_status.DesiredSize.Width, content);
        _source.Arrange(new Rect(left, footerTop, Math.Max(0, content - statusWidth - 6), _source.DesiredSize.Height));
        _status.Arrange(new Rect(right - statusWidth, footerTop, statusWidth, _status.DesiredSize.Height));

        var secondTop = footerTop + _source.DesiredSize.Height + 3;
        var trailing = right;
        if (_action is not null)
        {
            var width = Math.Min(_action.DesiredSize.Width, content);
            _action.Arrange(new Rect(trailing - width, secondTop, width, _metrics.RowMeta));
            trailing -= width + 6;
        }

        _heart.Arrange(new Rect(trailing - _metrics.RowLine, secondTop, _metrics.RowLine, _metrics.RowMeta));
        trailing -= _metrics.RowLine + 6;

        _subtitle.Arrange(new Rect(left, secondTop, Math.Max(0, trailing - left), _metrics.RowMeta));
        return finalSize;
    }

    private double DividerTop { get; set; }

    protected override void OnRender(DrawingContext context)
    {
        var bounds = new Rect(0, 0, NaturalWidth, RenderSize.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var shape = CardGeometry.Create(bounds, _metrics.CardCorner, _metrics.TailWidth, _metrics.TailHeight, TailCentre);
        context.DrawGeometry(Palette.Surface, null, shape);

        var hairline = DevicePixels.Snap(Metrics.Hairline, VisualTreeHelper.GetDpi(this).DpiScaleX);
        var pen = new Pen(Palette.Edge, hairline);
        pen.Freeze();
        context.DrawGeometry(null, pen, shape);

        context.DrawRectangle(Palette.Divider, null,
            new Rect(_metrics.CardPaddingH, DividerTop, _metrics.CardWidth - _metrics.CardPaddingH * 2, hairline));
    }

    /// <summary>macOS asks for black at 45 per cent, radius 17.6, offset (-5, 8).</summary>
    private static DropShadowEffect CardShadow(Metrics metrics)
    {
        var effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.45,
            BlurRadius = metrics.ShadowSlack * 0.8 * 2,
            ShadowDepth = Math.Sqrt(5 * 5 + 8 * 8),
            Direction = 237.99,
            RenderingBias = RenderingBias.Quality
        };
        effect.Freeze();
        return effect;
    }
}

/// <summary>The pin shown while a card is being kept open.</summary>
internal sealed class PinMark(double extent) : FrameworkElement
{
    protected override Size MeasureOverride(Size availableSize) => new(extent, extent);

    protected override void OnRender(DrawingContext context)
    {
        var side = Math.Min(RenderSize.Width, RenderSize.Height);
        if (side <= 0) return;
        context.PushTransform(new ScaleTransform(side, side));
        context.DrawGeometry(Palette.Tertiary, null, Marks.Pin);
        context.Pop();
    }
}

/// <summary>A provider mark on its own, for the card's header.</summary>
internal sealed class GlyphMark(ProviderGlyph glyph, double extent) : FrameworkElement
{
    protected override Size MeasureOverride(Size availableSize) => new(extent, extent);

    protected override void OnRender(DrawingContext context)
        => GlyphRenderer.Draw(context, glyph, new Rect(0, 0, RenderSize.Width, RenderSize.Height),
            Palette.Primary, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}

/// <summary>The card's one action: open the dashboard, or open settings.</summary>
internal sealed class CardAction : Panel
{
    private readonly TextBlock _title;
    private readonly TextBlock _chevron;

    public CardAction(string title, Typo typo)
    {
        _title = new TextBlock { Text = title, Foreground = Palette.Secondary }.Apply(typo.Action);
        // "↗" for a dashboard, "›" for settings, standing in for the SF Symbols macOS uses.
        _chevron = new TextBlock { Text = title == "Dashboard" ? "↗" : "›", Foreground = Palette.Secondary }
            .Apply(typo.ActionGlyph);

        Children.Add(_title);
        Children.Add(_chevron);
        Cursor = Cursors.Hand;
    }

    public event EventHandler? Activated;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        Activated?.Invoke(this, EventArgs.Empty);
        base.OnMouseLeftButtonUp(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _title.Measure(availableSize);
        _chevron.Measure(availableSize);
        return new Size(_title.DesiredSize.Width + 2 + _chevron.DesiredSize.Width,
            Math.Max(_title.DesiredSize.Height, _chevron.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _title.Arrange(new Rect(0, 0, _title.DesiredSize.Width, finalSize.Height));
        _chevron.Arrange(new Rect(_title.DesiredSize.Width + 2, 0, _chevron.DesiredSize.Width, finalSize.Height));
        return finalSize;
    }
}
