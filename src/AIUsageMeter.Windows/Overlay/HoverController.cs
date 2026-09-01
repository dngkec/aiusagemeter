using System.Windows.Threading;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Overlay;

/// <summary>Runs an action after a delay, and can be cancelled before it fires.</summary>
/// <remarks>Injected so the hover rules can be tested without waiting on a real clock.</remarks>
internal interface IDelayScheduler
{
    IDisposable After(TimeSpan delay, Action action);
}

/// <summary>The real one, on the interface thread.</summary>
internal sealed class DispatcherScheduler(Dispatcher dispatcher) : IDelayScheduler
{
    public IDisposable After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(delay, DispatcherPriority.Normal, (_, _) => { }, dispatcher);
        void Tick(object? sender, EventArgs e)
        {
            timer.Stop();
            timer.Tick -= Tick;
            action();
        }

        timer.Tick += Tick;
        timer.Start();
        return new Cancellation(() => { timer.Stop(); timer.Tick -= Tick; });
    }

    private sealed class Cancellation(Action stop) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            stop();
        }
    }
}

/// <summary>
/// When the overlay opens a card, when it lets one go, and when it shrinks to its idle tab.
/// </summary>
/// <remarks>
/// A port of the hover half of <c>AppModel</c>. <see cref="HoverTracker"/> decides what the pointer
/// is asking for; this decides how long to wait before believing it. The delays are the point: the
/// gap between the rail and the card is dead space the pointer must cross, and without the
/// hysteresis the card closes underneath it.
/// </remarks>
internal sealed class HoverController(IDelayScheduler scheduler)
{
    private readonly HoverTracker _pointer = new();
    private IDisposable? _hoverDelay;
    private IDisposable? _idleDelay;
    private ProviderId? _expanded;
    private ProviderId? _pinned;
    private bool _idleMini;

    /// <summary>Raised whenever the overlay should redraw itself.</summary>
    public event EventHandler? Changed;

    /// <summary>Which provider's card is open, if any.</summary>
    public ProviderId? Expanded
    {
        get => _expanded;
        private set
        {
            if (_expanded == value) return;
            _expanded = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The card the reader has clicked to keep open.</summary>
    public ProviderId? Pinned
    {
        get => _pinned;
        private set
        {
            if (_pinned == value) return;
            _pinned = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True once the overlay has shrunk to the tab at the edge of the screen.</summary>
    public bool IdleMini
    {
        get => _idleMini;
        private set
        {
            if (_idleMini == value) return;
            _idleMini = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Gauge(ProviderId id, bool inside)
    {
        _pointer.Gauge(id, inside);
        Apply();
    }

    public void Rail(bool inside)
    {
        _pointer.Rail(inside);
        Apply();
    }

    public void Card(bool inside)
    {
        _pointer.Card(inside);
        Apply();
    }

    /// <summary>Clicking a gauge keeps its card open, or lets go of one already kept.</summary>
    public void TogglePin(ProviderId id)
    {
        if (Pinned == id)
        {
            Pinned = null;
            ScheduleDismiss();
            return;
        }

        Pinned = id;
        Expanded = id;
        Cancel(ref _idleDelay);
    }

    /// <summary>Lets go of a pinned card, as the Escape key does.</summary>
    public void Unpin()
    {
        if (Pinned is null && Expanded is null) return;
        Pinned = null;
        Apply();
    }

    /// <summary>The pointer has found the idle tab, so bring the rail back.</summary>
    public void RevealFromMini()
    {
        Cancel(ref _idleDelay);
        IdleMini = false;
    }

    /// <summary>Forgets everything, as when the overlay is hidden.</summary>
    public void Reset()
    {
        Cancel(ref _hoverDelay);
        Cancel(ref _idleDelay);
        _pointer.Reset();
        Pinned = null;
        Expanded = null;
        IdleMini = false;
    }

    private void Apply()
    {
        Cancel(ref _hoverDelay);

        switch (_pointer.Decision)
        {
            case HoverDecision.Open:
                Cancel(ref _idleDelay);
                var wanted = _pointer.Target;
                _hoverDelay = scheduler.After(Motion.OpenDelay, () =>
                {
                    // The pointer may have moved on during the wait; only open what it still wants.
                    if (_pointer.Target == wanted) Expanded = wanted;
                });
                break;

            case HoverDecision.Keep:
                Cancel(ref _idleDelay);
                break;

            default:
                ScheduleDismiss();
                break;
        }
    }

    private void ScheduleDismiss()
    {
        if (Pinned is not null || Expanded is null)
        {
            ScheduleIdle();
            return;
        }

        _hoverDelay = scheduler.After(Motion.DismissDelay, () =>
        {
            if (_pointer.HoldsOpen || Pinned is not null) return;
            Expanded = null;
            ScheduleIdle();
        });
    }

    private void ScheduleIdle()
    {
        Cancel(ref _idleDelay);
        if (Pinned is not null || Expanded is not null || _pointer.HoldsOpen) return;

        _idleDelay = scheduler.After(Motion.IdleDelay, () =>
        {
            if (Pinned is not null || Expanded is not null || _pointer.HoldsOpen) return;
            IdleMini = true;
        });
    }

    private static void Cancel(ref IDisposable? delay)
    {
        delay?.Dispose();
        delay = null;
    }
}
