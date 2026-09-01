using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;
using AIUsageMeter.Windows.Interop;

namespace AIUsageMeter.Windows.Overlay;

/// <summary>
/// The overlay: a rail down the trailing edge, a card that opens beside whichever gauge the pointer
/// finds, and a tab it shrinks to when left alone.
/// </summary>
/// <remarks>
/// No XAML. Every part of this is drawn, so a markup file would hold a single child, and the
/// placement arithmetic lives in <see cref="OverlayLayout"/> where it can be tested without a window.
/// </remarks>
internal sealed class OverlayWindow : Window
{
    private readonly Canvas _surface = new();
    private readonly HoverController _hover;
    private readonly ScrollViewer _railScroll;
    private readonly MiniTab _tab;

    private Metrics _metrics;
    private Typo _typo;
    private RailPanel _rail;
    private DetailCard? _card;
    private IReadOnlyList<ProviderSnapshot> _snapshots = [];
    private AppPreferences _preferences;

    public OverlayWindow(AppPreferences preferences, IDelayScheduler scheduler, bool reduced = false)
    {
        _preferences = preferences;
        _metrics = Metrics.For(preferences.OverlaySize);
        _typo = Typo.For(preferences.OverlaySize);
        Reduced = reduced;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        Title = "AIUsageMeter";

        _hover = new HoverController(scheduler);
        _hover.Changed += (_, _) => Present();

        _rail = BuildRail();
        _railScroll = new ScrollViewer
        {
            Content = _rail,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = null,
            BorderThickness = new Thickness(0)
        };

        _tab = new MiniTab(_metrics);
        _tab.Revealed += (_, _) => _hover.RevealFromMini();

        // Transparent areas must let clicks through to whatever is behind the overlay.
        _surface.Background = null;
        _surface.Children.Add(_railScroll);
        _surface.Children.Add(_tab);
        Content = _surface;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) _hover.Unpin();
        };
    }

    public event EventHandler? SupportRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler<ProviderId>? DashboardRequested;

    /// <summary>Raised whenever the window's own size or position needs recomputing.</summary>
    public event EventHandler? PresentationChanged;

    public bool Reduced { get; }

    internal HoverController Hover => _hover;
    internal RailPanel Rail => _rail;
    internal Metrics Sizes => _metrics;

    /// <summary>How wide the window has to be for the rail, the card and the card's shadow.</summary>
    public double PanelWidth => _hover.IdleMini ? _metrics.MiniTarget : _metrics.PanelWidth;

    /// <summary>How tall, given the rail and the tallest card that could open beside it.</summary>
    public double PanelHeight => _hover.IdleMini
        ? _tab.Target.Height
        : Math.Max(_rail.NaturalHeight, TallestCard());

    public void Apply(AppPreferences preferences)
    {
        var resized = preferences.OverlaySize != _preferences.OverlaySize;
        _preferences = preferences;

        if (resized)
        {
            // A size change rebuilds every metric, so the rail is rebuilt with it.
            _metrics = Metrics.For(preferences.OverlaySize);
            _typo = Typo.For(preferences.OverlaySize);
            _rail = BuildRail();
            _railScroll.Content = _rail;
            _rail.SetProviders(_snapshots);
            RemoveCard();
        }

        Present();
    }

    public void Update(IReadOnlyList<ProviderSnapshot> snapshots, IReadOnlySet<ProviderId> refreshing)
    {
        _snapshots = snapshots;
        _rail.SetProviders(snapshots);
        _rail.SetRefreshing(refreshing);

        // A provider that has gone away cannot keep a card open.
        if (_hover.Expanded is { } open && snapshots.All(x => x.Id != open)) _hover.Reset();
        Present();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Never take focus from whatever the reader is working in.
        WindowStyles.MakeNonActivating(this);
    }

    private RailPanel BuildRail()
    {
        var rail = new RailPanel(_metrics, _typo, Reduced);
        rail.SupportRequested += (_, _) => SupportRequested?.Invoke(this, EventArgs.Empty);
        rail.SetupRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        rail.GaugeActivated += (_, id) => _hover.TogglePin(id);
        rail.MouseEnter += (_, _) => _hover.Rail(true);
        rail.MouseLeave += (_, _) => _hover.Rail(false);

        foreach (var gauge in rail.Gauges) Watch(gauge);
        return rail;
    }

    private void Watch(GaugeItem gauge)
    {
        gauge.MouseEnter += (_, _) => _hover.Gauge(gauge.Id, true);
        gauge.MouseLeave += (_, _) => _hover.Gauge(gauge.Id, false);
    }

    /// <summary>Lays the rail, the card and the tab out, then asks to be repositioned.</summary>
    internal void Present()
    {
        // Rows are rebuilt as providers change, so their hover wiring is refreshed here.
        foreach (var gauge in _rail.Gauges) Watch(gauge);

        var mini = _hover.IdleMini;
        _railScroll.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
        _tab.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;

        _rail.SetActive(_hover.Expanded);
        SyncCard();

        var width = PanelWidth;
        var height = PanelHeight;
        _surface.Width = width;
        _surface.Height = height;

        if (mini)
        {
            _tab.Width = _tab.Target.Width;
            _tab.Height = _tab.Target.Height;
            Canvas.SetLeft(_tab, width - _tab.Target.Width);
            Canvas.SetTop(_tab, (height - _tab.Target.Height) / 2);
        }
        else
        {
            // The rail hugs the trailing edge and centres on the panel.
            var railHeight = Math.Min(_rail.NaturalHeight, height);
            _railScroll.Width = _metrics.RailWidth;
            _railScroll.Height = railHeight;
            Canvas.SetLeft(_railScroll, width - _metrics.RailWidth);
            Canvas.SetTop(_railScroll, (height - railHeight) / 2);
        }

        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncCard()
    {
        var wanted = _hover.Expanded;
        var snapshot = wanted is null || _hover.IdleMini
            ? null
            : _snapshots.FirstOrDefault(x => x.Id == wanted);

        if (snapshot is null)
        {
            RemoveCard();
            return;
        }

        if (_card is null || _card.Id != snapshot.Id)
        {
            RemoveCard();
            _card = new DetailCard(snapshot, _metrics, _typo, DateTimeOffset.Now, Reduced);
            _card.SupportRequested += (_, _) => SupportRequested?.Invoke(this, EventArgs.Empty);
            _card.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            _card.DashboardRequested += (_, _) => DashboardRequested?.Invoke(this, snapshot.Id);
            _card.MouseEnter += (_, _) => _hover.Card(true);
            _card.MouseLeave += (_, _) => _hover.Card(false);
            _surface.Children.Add(_card);
        }

        _card.Pinned = _hover.Pinned == snapshot.Id;
        PlaceCard(_card, snapshot);
    }

    /// <summary>Puts the card beside its own gauge, and aims the tail back at it.</summary>
    internal void PlaceCard(DetailCard card, ProviderSnapshot snapshot)
    {
        var height = PanelHeight;
        var railHeight = Math.Min(_rail.NaturalHeight, height);
        var railTop = (height - railHeight) / 2;

        // The gauge's centre, in panel coordinates, then as an offset from the middle of the panel.
        var gaugeCentre = railTop + _rail.GaugeCentre(snapshot.Id) - height / 2;
        var cardHeight = card.NaturalHeight;
        var placement = OverlayLayout.CardPlacement(gaugeCentre, cardHeight, height, _metrics.TailInset);

        card.Width = card.NaturalWidth;
        card.Height = cardHeight;
        card.TailCentre = placement.TailCentre;

        // The tail stops a tail-gap short of the rail, which leaves the shadow room on the left.
        Canvas.SetLeft(card, PanelWidth - _metrics.RailWidth - _metrics.TailGap - card.NaturalWidth);
        Canvas.SetTop(card, height / 2 + placement.Centre - cardHeight / 2);
    }

    private void RemoveCard()
    {
        if (_card is null) return;
        _surface.Children.Remove(_card);
        _card = null;
    }

    /// <summary>The tallest card any visible provider could open, so the window is never too short.</summary>
    private double TallestCard()
        => _snapshots.Count == 0 ? 0 : _snapshots.Max(x => _metrics.Card.Height(x));
}
