using AIUsageMeter.Core;

namespace AIUsageMeter.Windows.Overlay;

/// <summary>
/// The card's time wording, mirroring <c>RelativeTime</c> in
/// <c>Sources/AIUsageMeter/NotchViews.swift</c>.
/// </summary>
internal static class RelativeTime
{
    /// <summary>How long ago a reading was taken, for the card's header.</summary>
    public static string Short(DateTimeOffset moment, DateTimeOffset now)
    {
        var delta = now - moment;
        if (delta.TotalSeconds < 45) return "just now";
        if (delta.TotalSeconds < 3600) return $"{(int)(delta.TotalSeconds / 60)} min ago";
        if (delta.TotalSeconds < 86_400) return $"{(int)(delta.TotalSeconds / 3600)} h ago";
        return $"{(int)(delta.TotalSeconds / 86_400)} d ago";
    }

    /// <summary>A wall-clock time in the reader's own format.</summary>
    public static string Clock(DateTimeOffset moment) => moment.ToLocalTime().ToString("t");

    /// <summary>When a usage window next resets.</summary>
    public static string Reset(DateTimeOffset moment, DateTimeOffset now)
    {
        var delta = moment - now;
        if (delta.TotalSeconds <= 0) return "Resetting…";
        if (delta.TotalSeconds < 3600) return $"Resets in {Math.Max(1, (int)(delta.TotalSeconds / 60))} min";
        if (delta.TotalHours < 12) return $"Resets {Clock(moment)}";
        // The weekday, then the same locale clock as the line above. Hardcoding 24-hour here put a
        // "Resets Thu 16:34" directly under a "Resets 6:34 PM" on the same card.
        return $"Resets {moment.ToLocalTime():ddd} {Clock(moment)}";
    }

    /// <summary>The reset line for one usage window, or why there is none.</summary>
    public static string ResetLine(UsageWindow window, DateTimeOffset now)
        => window.ResetsAt is { } resetsAt ? Reset(resetsAt, now) : "No reset scheduled";
}

/// <summary>Wording for a provider's status, mirroring the macOS extension on <c>ProviderStatus</c>.</summary>
internal static class StatusWording
{
    public static string ShortLabel(this ProviderStatus status) => status switch
    {
        ProviderStatus.Ready => "Ready",
        ProviderStatus.Loading => "Refreshing",
        ProviderStatus.SetupNeeded => "Setup needed",
        ProviderStatus.Offline => "Offline",
        ProviderStatus.Unauthorized or ProviderStatus.Expired => "Sign-in needed",
        ProviderStatus.RateLimited => "Rate limited",
        _ => "Unavailable"
    };

    /// <summary>Whether the card should offer a way into settings rather than a dashboard link.</summary>
    public static bool NeedsAttention(this ProviderStatus status)
        => status is ProviderStatus.SetupNeeded or ProviderStatus.Unauthorized or ProviderStatus.Expired;

    /// <summary>
    /// The colour of the status word in the footer. macOS borrows the usage thresholds rather than
    /// inventing a second palette: green for ready, amber for the ones that will clear on their own,
    /// red for the ones that will not.
    /// </summary>
    public static Brush Tint(this ProviderStatus status) => status switch
    {
        ProviderStatus.Ready => Design.Palette.Usage(0),
        ProviderStatus.Loading => Design.Palette.Secondary,
        ProviderStatus.RateLimited or ProviderStatus.SetupNeeded => Design.Palette.Usage(75),
        _ => Design.Palette.Usage(95)
    };
}
