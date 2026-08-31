using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsageMeter.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ProviderMode>))]
public enum ProviderMode { Live, CustomJson, Manual }
[JsonConverter(typeof(JsonStringEnumConverter<ProviderRegion>))]
public enum ProviderRegion { Global, China }
[JsonConverter(typeof(JsonStringEnumConverter<VerticalPosition>))]
public enum VerticalPosition { Top, Center, Bottom }
[JsonConverter(typeof(JsonStringEnumConverter<OverlaySize>))]
public enum OverlaySize { Compact, Medium, Large }
[JsonConverter(typeof(JsonStringEnumConverter<HttpVerb>))]
public enum HttpVerb { Get, Post }
[JsonConverter(typeof(JsonStringEnumConverter<SecretPlacement>))]
public enum SecretPlacement { Bearer, ApiKeyHeader, None }

public sealed record ManualBudget(double Used = 0, double Limit = 100, DateTimeOffset? ResetDate = null);
public sealed record CustomConnector(
    string Name = "Custom", string Endpoint = "", HttpVerb Method = HttpVerb.Get,
    SecretPlacement SecretPlacement = SecretPlacement.Bearer, string ApiKeyHeader = "X-API-Key",
    string PercentPath = "usage.percent", string UsedPath = "usage.used",
    string LimitPath = "usage.limit", string ResetPath = "usage.resets_at", string DashboardUrl = "");

public sealed record ProviderConfiguration(
    ProviderId Id,
    bool Enabled = false,
    bool ShowInOverlay = true,
    ProviderMode Mode = ProviderMode.Live,
    double MonthlyBudget = 100,
    string WorkspaceId = "",
    ProviderRegion Region = ProviderRegion.Global,
    ManualBudget? Manual = null,
    CustomConnector? Custom = null)
{
    public ManualBudget ManualValue => Manual ?? new();
    public CustomConnector CustomValue => Custom ?? new();
}

public sealed record AppPreferences(
    int SchemaVersion,
    IReadOnlyList<ProviderConfiguration> Providers,
    double RefreshIntervalSeconds = 300,
    string? ScreenIdentifier = null,
    VerticalPosition VerticalPosition = VerticalPosition.Center,
    double VerticalOffset = 0,
    bool LaunchAtLogin = false,
    bool DemoData = false,
    bool OverlayVisible = true,
    OverlaySize OverlaySize = OverlaySize.Medium)
{
    public const int CurrentSchemaVersion = 2;
    public static AppPreferences Defaults => new(CurrentSchemaVersion,
        ProviderInfo.All.Select(id => new ProviderConfiguration(id, id is ProviderId.Claude or ProviderId.Codex or ProviderId.Grok)).ToList());
}

public static class PreferencesMigration
{
    public static AppPreferences Migrate(AppPreferences? input)
    {
        var value = input ?? AppPreferences.Defaults;
        var providers = value.Providers.GroupBy(x => x.Id).Select(x => x.First()).ToList();
        var seen = providers.Select(x => x.Id).ToHashSet();
        providers.AddRange(ProviderInfo.All.Where(x => !seen.Contains(x)).Select(x => new ProviderConfiguration(x)));
        return value with
        {
            SchemaVersion = AppPreferences.CurrentSchemaVersion,
            Providers = providers,
            RefreshIntervalSeconds = Math.Clamp(value.RefreshIntervalSeconds, 30, 86_400),
            VerticalOffset = Math.Clamp(value.VerticalOffset, -2_000, 2_000)
        };
    }
}

public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly string _path;

    public PreferencesStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageMeter", "preferences.json");

    public AppPreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return AppPreferences.Defaults;
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            return PreferencesMigration.Migrate(JsonSerializer.Deserialize<AppPreferences>(stream, JsonOptions));
        }
        catch (JsonException) { return AppPreferences.Defaults; }
        catch (IOException) { return AppPreferences.Defaults; }
        catch (UnauthorizedAccessException) { return AppPreferences.Defaults; }
    }

    public async Task SaveAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        var migrated = PreferencesMigration.Migrate(preferences);
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Preferences path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            await JsonSerializer.SerializeAsync(stream, migrated, JsonOptions, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, _path, true);
    }
}
