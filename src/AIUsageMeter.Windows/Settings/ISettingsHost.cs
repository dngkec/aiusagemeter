using AIUsageMeter.Core;

namespace AIUsageMeter.Windows;

internal abstract record SettingsSelection
{
    public sealed record General : SettingsSelection;
    public sealed record About : SettingsSelection;
    public sealed record Provider(ProviderId Id) : SettingsSelection;
}

internal sealed record SettingsNotice(string Text, bool IsFailure)
{
    public static SettingsNotice Success(string text) => new(text, false);
    public static SettingsNotice Failure(string text) => new(text, true);
}

/// <summary>
/// The seam Settings talks to. <see cref="AppController"/> applies, persists, and refreshes.
/// </summary>
internal interface ISettingsHost
{
    IReadOnlyList<ProviderSnapshot> Snapshots { get; }
    DateTimeOffset? LastRefresh { get; }
    bool IsRefreshing { get; }
    string? PersistError { get; }
    void Apply(AppPreferences preferences, bool? refetch = null);
    Task RefreshNowAsync();
    event EventHandler? HostChanged;
}
