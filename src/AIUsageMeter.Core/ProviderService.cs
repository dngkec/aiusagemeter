using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AIUsageMeter.Core;

public sealed record ProviderContext(IUsageHttpClient Http, ISecretStore Secrets, ICredentialDiscovery Credentials);

public interface IProviderFetcher
{
    Task<ProviderSnapshot> FetchAsync(ProviderConfiguration configuration, CancellationToken cancellationToken);
}

public sealed class ProviderService(ProviderContext context) : IProviderFetcher
{
    private const int OneMb = 1_000_000;
    private const int TwoMb = 2_000_000;

    public async Task<ProviderSnapshot> FetchAsync(ProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.Mode == ProviderMode.Manual)
        {
            var budget = configuration.ManualValue;
            return Snapshot(configuration.Id, [new("manual", "Manual budget", budget.Used, budget.Limit, budget.ResetDate)], DataSourceKind.Manual, "Entered manually");
        }
        if (configuration.Mode == ProviderMode.CustomJson) return await FetchCustomAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await FetchLiveAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProviderSnapshot> FetchCustomAsync(ProviderConfiguration configuration, CancellationToken cancellationToken)
    {
        var connector = configuration.CustomValue; var uri = EndpointPolicy.Validate(connector.Endpoint);
        var secret = context.Secrets.Read($"custom.{configuration.Id}");
        var headers = new Dictionary<string, string>(); string? bearer = null;
        if (connector.SecretPlacement == SecretPlacement.Bearer) bearer = secret;
        if (connector.SecretPlacement == SecretPlacement.ApiKeyHeader && !string.IsNullOrWhiteSpace(secret)) headers[connector.ApiKeyHeader] = secret;
        using var request = RequestFactory.Create(uri, connector.Method == HttpVerb.Post ? HttpMethod.Post : HttpMethod.Get, bearer, headers,
            connector.Method == HttpVerb.Post ? JsonContent.Create(new { }) : null);
        var data = await context.Http.SendAsync(request, TwoMb, cancellationToken).ConfigureAwait(false);
        var dashboard = string.IsNullOrWhiteSpace(connector.DashboardUrl) ? null : EndpointPolicy.Validate(connector.DashboardUrl);
        return Snapshot(configuration.Id, UsageParsers.Custom(data, connector), DataSourceKind.CustomJson, "Configured endpoint", dashboard, connector.Name);
    }

    private async Task<ProviderSnapshot> FetchLiveAsync(ProviderConfiguration config, CancellationToken token)
    {
        switch (config.Id)
        {
            case ProviderId.Claude:
            {
                var credential = context.Credentials.ReadJson(".claude/.credentials.json", "AppData/Roaming/Claude Code/credentials.json");
                var key = RequiredToken(credential, "claudeAiOauth.accessToken", "accessToken", "access_token");
                var data = await Get("https://api.anthropic.com/api/oauth/usage", key, OneMb, token,
                    new Dictionary<string, string> { ["anthropic-beta"] = "oauth-2025-04-20", ["anthropic-version"] = "2023-06-01" }).ConfigureAwait(false);
                return Snapshot(config.Id, UsageParsers.Claude(data), dashboard: "https://claude.ai/settings/usage");
            }
            case ProviderId.Codex:
            {
                var credential = context.Credentials.ReadJson(".codex/auth.json"); var key = RequiredToken(credential, "tokens.access_token", "access_token");
                var headers = new Dictionary<string, string>(); var account = credential.String("tokens.account_id", "account_id");
                if (!string.IsNullOrWhiteSpace(account)) headers["ChatGPT-Account-Id"] = account;
                var data = await Get("https://chatgpt.com/backend-api/wham/usage", key, OneMb, token, headers).ConfigureAwait(false);
                return Snapshot(config.Id, UsageParsers.Codex(data), dashboard: "https://chatgpt.com/codex/settings/usage");
            }
            case ProviderId.Grok:
            {
                var credential = context.Credentials.ReadJson(".grok/auth.json"); var key = credential.RecursiveToken() ?? throw MissingToken();
                var headers = new Dictionary<string, string> { ["x-xai-token-auth"] = "xai-grok-cli" };
                var first = TryGet("https://cli-chat-proxy.grok.com/v1/billing", key, TwoMb, token, headers);
                var second = TryGet("https://cli-chat-proxy.grok.com/v1/billing?format=credits", key, TwoMb, token, headers);
                await Task.WhenAll(first, second).ConfigureAwait(false);
                if (first.Result is null && second.Result is null) throw new UsageMeterException("Grok usage is unavailable.", UsageErrorKind.InvalidResponse);
                return Snapshot(config.Id, UsageParsers.Grok(first.Result, second.Result), dashboard: "https://grok.com");
            }
            case ProviderId.Copilot:
            {
                var key = context.Credentials.ReadCopilotToken();
                var data = await Get("https://api.github.com/copilot_internal/user", key, OneMb, token,
                    new Dictionary<string, string> { ["X-GitHub-Api-Version"] = "2025-04-01", ["Editor-Version"] = "vscode/1.90.0", ["Editor-Plugin-Version"] = "copilot/1.0.0" }).ConfigureAwait(false);
                return Snapshot(config.Id, UsageParsers.Copilot(data), dashboard: "https://github.com/settings/copilot");
            }
            case ProviderId.Gemini:
            {
                var credential = context.Credentials.ReadJson(".gemini/oauth_creds.json"); var key = RequiredToken(credential, "access_token");
                var load = await Post("https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist", key, new { metadata = new { ideType = "GEMINI_CLI", pluginType = "GEMINI" } }, OneMb, token).ConfigureAwait(false);
                using var loadJson = JsonDocument.Parse(load); string? project = null;
                if (loadJson.RootElement.TryGetProperty("cloudaicompanionProject", out var projectElement)) project = projectElement.GetString();
                var quota = await Post("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota", key, project is null ? new { } : new { project }, TwoMb, token).ConfigureAwait(false);
                return Snapshot(config.Id, UsageParsers.Gemini(quota), dashboard: "https://gemini.google.com");
            }
            case ProviderId.Kimi:
            {
                var credential = context.Credentials.ReadJson(".kimi-code/credentials/kimi-code.json"); var key = RequiredToken(credential, "access_token");
                var data = await Get("https://api.kimi.com/coding/v1/usages", key, TwoMb, token).ConfigureAwait(false);
                return Snapshot(config.Id, UsageParsers.Kimi(data), dashboard: "https://www.kimi.com/code");
            }
            case ProviderId.AnthropicCost:
            {
                var key = RequiredSecret(config.Id); var start = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
                var url = $"https://api.anthropic.com/v1/organizations/cost_report?starting_at={Uri.EscapeDataString(start.ToString("O"))}&ending_at={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&bucket_width=1d&limit=31";
                var data = await Get(url, null, TwoMb, token, new Dictionary<string, string> { ["x-api-key"] = key, ["anthropic-version"] = "2023-06-01" }).ConfigureAwait(false);
                return Snapshot(config.Id, UsageParsers.AnthropicCost(data, config.MonthlyBudget), dashboard: "https://console.anthropic.com/settings/billing");
            }
            case ProviderId.OpenAIAPI:
            {
                var start = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
                var data = await Get($"https://api.openai.com/v1/organization/costs?start_time={start}&limit=31", RequiredSecret(config.Id), TwoMb, token).ConfigureAwait(false);
                return Snapshot(config.Id, UsageParsers.OpenAICost(data, config.MonthlyBudget), dashboard: "https://platform.openai.com/usage");
            }
            case ProviderId.OpenRouter:
            {
                var key = RequiredSecret(config.Id); var credits = TryGet("https://openrouter.ai/api/v1/credits", key, OneMb, token); var details = TryGet("https://openrouter.ai/api/v1/key", key, OneMb, token);
                await Task.WhenAll(credits, details).ConfigureAwait(false);
                if (credits.Result is null && details.Result is null) throw new UsageMeterException("OpenRouter usage is unavailable.", UsageErrorKind.InvalidResponse);
                return Snapshot(config.Id, UsageParsers.OpenRouter(credits.Result, details.Result, config.MonthlyBudget), dashboard: "https://openrouter.ai/credits");
            }
            case ProviderId.DeepSeek:
                return Snapshot(config.Id, UsageParsers.DeepSeek(await Get("https://api.deepseek.com/user/balance", RequiredSecret(config.Id), OneMb, token).ConfigureAwait(false), config.MonthlyBudget), dashboard: "https://platform.deepseek.com/usage");
            case ProviderId.Mistral:
                return Snapshot(config.Id, UsageParsers.Mistral(await Get("https://api.mistral.ai/v1/admin/spend-limit", null, OneMb, token, new Dictionary<string, string> { ["x-api-key"] = RequiredSecret(config.Id) }).ConfigureAwait(false), config.MonthlyBudget), dashboard: "https://admin.mistral.ai/organization/usage");
            case ProviderId.XaiAPI:
            {
                if (string.IsNullOrWhiteSpace(config.WorkspaceId) || config.WorkspaceId.Any(x => !char.IsLetterOrDigit(x) && x is not '-' and not '_'))
                    throw new UsageMeterException("Add your xAI team ID in Settings.", UsageErrorKind.SetupNeeded);
                var data = await Get($"https://management-api.x.ai/v1/billing/teams/{config.WorkspaceId}/prepaid/balance", RequiredSecret(config.Id), TwoMb, token).ConfigureAwait(false);
                return Snapshot(config.Id, UsageParsers.XaiBalance(data, config.MonthlyBudget), dashboard: "https://console.x.ai");
            }
            case ProviderId.Moonshot:
            {
                var host = config.Region == ProviderRegion.China ? "https://api.moonshot.cn" : "https://api.moonshot.ai";
                var data = await Get(host + "/v1/users/me/balance", RequiredSecret(config.Id), OneMb, token).ConfigureAwait(false);
                return Snapshot(config.Id, UsageParsers.Moonshot(data, config.MonthlyBudget), dashboard: config.Region == ProviderRegion.China ? "https://platform.kimi.com/console/account" : "https://platform.moonshot.ai/console/account");
            }
            case ProviderId.Zai:
            {
                var host = config.Region == ProviderRegion.China ? "https://open.bigmodel.cn" : "https://api.z.ai";
                var data = await Get(host + "/api/monitor/usage/quota/limit", RequiredSecret(config.Id), TwoMb, token).ConfigureAwait(false);
                return Snapshot(config.Id, UsageParsers.Zai(data), dashboard: config.Region == ProviderRegion.China ? "https://bigmodel.cn/coding-plan/personal/usage" : "https://z.ai/manage-apikey/coding-plan/personal/my-plan");
            }
            case ProviderId.OpenCode:
                return Snapshot(config.Id, UsageParsers.OpenCode(await Get("https://opencode.ai/zen/go/v1/usage", RequiredSecret(config.Id), TwoMb, token).ConfigureAwait(false)), dashboard: "https://opencode.ai");
            case ProviderId.Cursor:
                throw Unavailable("Cursor stores its token in state.vscdb. The Windows build does not ship or invoke a SQLite reader; use Custom JSON or Manual Budget.");
            case ProviderId.JetBrainsAI:
                throw Unavailable("JetBrains AI quota files are not yet available through the Windows connector; use Manual Budget.");
            case ProviderId.Warp:
                throw Unavailable("Warp's Windows usage integration is unavailable because Warp does not currently expose the app integration used on macOS.");
            default:
                throw Unavailable("No safe built-in usage endpoint is available on Windows. Choose Custom JSON or Manual Budget.");
        }
    }

    private string RequiredSecret(ProviderId id)
    {
        var account = SecretAccounts.For(id) ?? throw new InvalidOperationException("Provider has no app-owned secret account.");
        return context.Secrets.Read(account) is { Length: > 0 } secret ? secret : throw new UsageMeterException($"Add the {id.DisplayName()} key in Settings.", UsageErrorKind.SetupNeeded);
    }

    private async Task<byte[]> Get(string url, string? bearer, int maximumBytes, CancellationToken token, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = RequestFactory.Create(EndpointPolicy.Validate(url), HttpMethod.Get, bearer, headers);
        return await context.Http.SendAsync(request, maximumBytes, token).ConfigureAwait(false);
    }

    private async Task<byte[]?> TryGet(string url, string? bearer, int maximumBytes, CancellationToken token, IReadOnlyDictionary<string, string>? headers = null)
    {
        try { return await Get(url, bearer, maximumBytes, token, headers).ConfigureAwait(false); }
        catch (UsageMeterException) { return null; }
    }

    private async Task<byte[]> Post(string url, string bearer, object body, int maximumBytes, CancellationToken token)
    {
        using var request = RequestFactory.Create(EndpointPolicy.Validate(url), HttpMethod.Post, bearer, content: JsonContent.Create(body));
        return await context.Http.SendAsync(request, maximumBytes, token).ConfigureAwait(false);
    }

    private static ProviderSnapshot Snapshot(ProviderId id, IReadOnlyList<UsageWindow> windows, DataSourceKind source = DataSourceKind.Live,
        string? message = null, string? dashboard = null, string? customName = null) => new(id, windows, Source: source, Message: message,
            DashboardUrl: dashboard is null ? null : new Uri(dashboard), UpdatedAt: DateTimeOffset.UtcNow, CustomName: customName);
    private static ProviderSnapshot Snapshot(ProviderId id, IReadOnlyList<UsageWindow> windows, DataSourceKind source, string? message, Uri? dashboard, string? customName) =>
        new(id, windows, Source: source, Message: message, DashboardUrl: dashboard, UpdatedAt: DateTimeOffset.UtcNow, CustomName: customName);
    private static string RequiredToken(DiscoveredCredential credential, params string[] paths) => credential.String(paths) ?? throw MissingToken();
    private static UsageMeterException MissingToken() => new("The saved credential does not contain an access token.", UsageErrorKind.SetupNeeded);
    private static UsageMeterException Unavailable(string message) => new(message, UsageErrorKind.SetupNeeded);
}
