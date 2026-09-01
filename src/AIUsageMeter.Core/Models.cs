using System.Text.Json.Serialization;

namespace AIUsageMeter.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ProviderId>))]
public enum ProviderId
{
    Claude, AnthropicCost, Codex, Grok, Cursor, Copilot, Gemini, Kimi,
    OpenAIAPI, OpenRouter, DeepSeek, Mistral, XaiAPI, Moonshot, Perplexity,
    Windsurf, Zai, OpenCode, LocalModels, JetBrainsAI, Warp, Amp, Kilo,
    Augment, Devin, Antigravity, Custom
}

public static class ProviderInfo
{
    public static readonly ProviderId[] All = Enum.GetValues<ProviderId>();

    public static string DisplayName(this ProviderId id) => id switch
    {
        ProviderId.Claude => "Claude Code", ProviderId.AnthropicCost => "Anthropic API",
        ProviderId.Codex => "Codex / ChatGPT", ProviderId.Grok => "Grok / xAI",
        ProviderId.Cursor => "Cursor", ProviderId.Copilot => "GitHub Copilot",
        ProviderId.Gemini => "Gemini Code Assist", ProviderId.Kimi => "Kimi Code",
        ProviderId.OpenAIAPI => "OpenAI API", ProviderId.OpenRouter => "OpenRouter",
        ProviderId.DeepSeek => "DeepSeek", ProviderId.Mistral => "Mistral",
        ProviderId.XaiAPI => "xAI Platform", ProviderId.Moonshot => "Moonshot / Kimi",
        ProviderId.Perplexity => "Perplexity", ProviderId.Windsurf => "Windsurf",
        ProviderId.Zai => "Z.ai / GLM", ProviderId.OpenCode => "OpenCode",
        ProviderId.LocalModels => "Ollama / LM Studio", ProviderId.JetBrainsAI => "JetBrains AI",
        ProviderId.Warp => "Warp", ProviderId.Amp => "Amp", ProviderId.Kilo => "Kilo",
        ProviderId.Augment => "Augment", ProviderId.Devin => "Devin",
        ProviderId.Antigravity => "Antigravity", _ => "Custom"
    };

    public static string Monogram(this ProviderId id) => id switch
    {
        ProviderId.Claude => "✳", ProviderId.AnthropicCost => "A",
        ProviderId.Codex or ProviderId.OpenAIAPI => "◎", ProviderId.Grok => "𝕏",
        ProviderId.Cursor => "C", ProviderId.Copilot => "GH", ProviderId.Gemini => "✦",
        ProviderId.Kimi => "K", ProviderId.OpenRouter => "OR", ProviderId.DeepSeek => "DS",
        ProviderId.Mistral => "M", ProviderId.XaiAPI => "xAI", ProviderId.Moonshot => "MS",
        ProviderId.Perplexity => "P", ProviderId.Windsurf => "W", ProviderId.Zai => "Z",
        ProviderId.OpenCode => "OC", ProviderId.LocalModels => "L", ProviderId.JetBrainsAI => "JB",
        ProviderId.Warp => "W", ProviderId.Amp => "A", ProviderId.Kilo => "Ki",
        ProviderId.Augment => "AU", ProviderId.Devin => "D", ProviderId.Antigravity => "AG", _ => "+"
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<ProviderStatus>))]
public enum ProviderStatus { Ready, Loading, SetupNeeded, Offline, Unauthorized, RateLimited, Error, Expired }
[JsonConverter(typeof(JsonStringEnumConverter<DataSourceKind>))]
public enum DataSourceKind { Live, CustomJson, Manual, Demo }
[JsonConverter(typeof(JsonStringEnumConverter<UsageKind>))]
public enum UsageKind { Quota, ExtraUsage, ApiCost, Credits }

public sealed record UsageWindow(
    string Id,
    string Label,
    double Used,
    double Limit,
    DateTimeOffset? ResetsAt = null,
    UsageKind Kind = UsageKind.Quota)
{
    public double SafeUsed => double.IsFinite(Used) ? Math.Max(0, Used) : 0;
    public double SafeLimit => double.IsFinite(Limit) ? Math.Max(0, Limit) : 0;
    public double Fraction => SafeLimit > 0 ? SafeUsed / SafeLimit : 0;
    public double Percent => Fraction * 100;
    public string ReadingCaption => Kind == UsageKind.Quota
        ? $"{Percent:F0}%"
        : Kind == UsageKind.Credits && SafeUsed == Math.Round(SafeUsed) && SafeLimit == Math.Round(SafeLimit)
            ? $"{SafeUsed:F0} / {SafeLimit:F0}"
            : $"{Money(SafeUsed)} / {Money(SafeLimit)}";

    private static string Money(double value) => value == Math.Round(value)
        ? $"${value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}"
        : $"${value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}";
}

public sealed record ProviderSnapshot(
    ProviderId Id,
    IReadOnlyList<UsageWindow> Windows,
    ProviderStatus Status = ProviderStatus.Ready,
    DataSourceKind Source = DataSourceKind.Live,
    string? Message = null,
    Uri? DashboardUrl = null,
    DateTimeOffset? UpdatedAt = null,
    string? CustomName = null)
{
    public string Name => string.IsNullOrWhiteSpace(CustomName) ? Id.DisplayName() : CustomName;
    public double PrimaryPercent => Windows.FirstOrDefault()?.Percent ?? 0;
    public DateTimeOffset Timestamp => UpdatedAt ?? DateTimeOffset.UtcNow;

    public IReadOnlyList<UsageWindow> FeaturedWindows(int limit = 3)
    {
        if (Windows.Count <= limit) return Windows;
        var extras = Windows.Where(x => x.Kind != UsageKind.Quota).Take(limit).ToList();
        return Windows.Where(x => x.Kind == UsageKind.Quota).Take(limit - extras.Count).Concat(extras).ToList();
    }
}

public static class UsageColor
{
    public static string For(double percent) => percent switch
    {
        < 50 => "#14FF97", < 70 => "#EDFF05", < 90 => "#FF9F0A", _ => "#FF453A"
    };
}

public sealed class UsageMeterException(string message, UsageErrorKind kind = UsageErrorKind.InvalidResponse) : Exception(message)
{
    public UsageErrorKind Kind { get; } = kind;
}

public enum UsageErrorKind { SetupNeeded, Unauthorized, RateLimited, Server, Offline, Timeout, OversizedResponse, InvalidResponse, ExpiredCredential, InvalidUrl, MissingField }
