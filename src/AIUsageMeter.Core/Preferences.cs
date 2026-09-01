using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsageMeter.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ProviderMode>))]
public enum ProviderMode { Live, CustomJson, Manual }
[JsonConverter(typeof(JsonStringEnumConverter<ProviderRegion>))]
public enum ProviderRegion { Global, China }
[JsonConverter(typeof(JsonStringEnumConverter<VerticalPosition>))]
public enum VerticalPosition { Top, Center, Bottom }
[JsonConverter(typeof(OverlaySizeJsonConverter))]
public enum OverlaySize { Small, Medium, Large }

public static class OverlaySizeExtensions
{
    /// <summary>Multiplier applied to every metric in the overlay, matching the macOS build.</summary>
    public static double Scale(this OverlaySize size) => size switch
    {
        OverlaySize.Small => 0.86,
        OverlaySize.Large => 1.18,
        _ => 1.0
    };
}

/// <summary>Reads <see cref="OverlaySize"/>, accepting "Compact" as the retired name for Small.</summary>
public sealed class OverlaySizeJsonConverter : JsonConverter<OverlaySize>
{
    public override OverlaySize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Overlay size must be a string.");
        var text = reader.GetString();
        if (string.Equals(text, "Compact", StringComparison.OrdinalIgnoreCase)) return OverlaySize.Small;
        return Enum.TryParse<OverlaySize>(text, ignoreCase: true, out var value)
            ? value
            : throw new JsonException($"Unknown overlay size '{text}'.");
    }

    // Shipped files were written by the options-level camelCase enum converter, so keep that spelling.
    public override void Write(Utf8JsonWriter writer, OverlaySize value, JsonSerializerOptions options)
        => writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
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

/// <summary>
/// How far the rail may be nudged from its vertical position, in DIP. One range for the slider,
/// the settings working copy, and the stored file: a larger stored value used to keep moving the
/// overlay while Settings showed a number it had silently clamped.
/// </summary>
public static class OverlayOffset
{
    public const double Min = -300;
    public const double Max = 300;

    public static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, Min, Max) : 0;
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
            VerticalOffset = OverlayOffset.Clamp(value.VerticalOffset)
        };
    }
}

public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Ordered: options-level converters outrank a type's own [JsonConverter], and the first
        // match wins, so the overlay-size converter has to come before the catch-all enum one.
        Converters = { new OverlaySizeJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
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
