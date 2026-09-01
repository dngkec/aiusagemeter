namespace AIUsageMeter.Core;

/// <summary>What the pointer's current position says the overlay should do.</summary>
public enum HoverDecision
{
    /// <summary>Open the card for <see cref="HoverTracker.Target"/>.</summary>
    Open,

    /// <summary>Hold whatever is open; the pointer is still on the overlay.</summary>
    Keep,

    /// <summary>Nothing is under the pointer; start the dismiss delay.</summary>
    Dismiss
}

/// <summary>
/// Enter and exit arrive in either order and one can go missing, so the last gauge entered wins.
/// </summary>
/// <remarks>
/// The macOS original is a mutating struct. A mutable struct is the wrong shape in C#: every
/// assignment copies, so a caller that stashes one silently stops seeing updates. Sealed class.
/// </remarks>
public sealed class HoverTracker
{
    private readonly List<ProviderId> _gauges = [];
    private bool _onRail;
    private bool _onCard;

    /// <summary>The gauge whose card should be open, or null when the pointer is on no gauge.</summary>
    public ProviderId? Target => _gauges.Count > 0 ? _gauges[^1] : null;

    /// <summary>What the current pointer position asks for.</summary>
    public HoverDecision Decision =>
        Target is not null ? HoverDecision.Open
        : _onRail || _onCard ? HoverDecision.Keep
        : HoverDecision.Dismiss;

    /// <summary>True while the pointer is anywhere that should hold the card open.</summary>
    public bool HoldsOpen => Decision != HoverDecision.Dismiss;

    /// <summary>Records the pointer entering or leaving a gauge. Entering also implies the rail.</summary>
    public void Gauge(ProviderId id, bool inside)
    {
        _gauges.RemoveAll(x => x == id);
        if (!inside) return;
        _gauges.Add(id);
        _onRail = true;
    }

    /// <summary>Records the pointer entering or leaving the rail. Leaving forgets every gauge.</summary>
    public void Rail(bool inside)
    {
        _onRail = inside;
        if (!inside) _gauges.Clear();
    }

    /// <summary>Records the pointer entering or leaving the detail card.</summary>
    public void Card(bool inside) => _onCard = inside;

    /// <summary>Forgets everything, as when the overlay is hidden.</summary>
    public void Reset()
    {
        _gauges.Clear();
        _onRail = false;
        _onCard = false;
    }
}
