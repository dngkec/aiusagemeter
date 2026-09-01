namespace AIUsageMeter.Core;

/// <summary>
/// Which built-in fields a provider's live source needs. Mirrors <c>LiveCredential</c> on macOS.
/// Account names live on <see cref="SecretAccounts"/> so both platforms share one table.
/// </summary>
public static class LiveCredential
{
    public static bool UsesMonthlyBudget(ProviderId id) => id is
        ProviderId.AnthropicCost or ProviderId.OpenAIAPI or ProviderId.OpenRouter
        or ProviderId.DeepSeek or ProviderId.Mistral or ProviderId.XaiAPI or ProviderId.Moonshot;

    public static string Prompt(ProviderId id) => id switch
    {
        ProviderId.AnthropicCost or ProviderId.OpenAIAPI or ProviderId.Mistral => "Admin key",
        ProviderId.XaiAPI => "Management key",
        ProviderId.OpenRouter or ProviderId.DeepSeek or ProviderId.Moonshot
            or ProviderId.Zai or ProviderId.OpenCode or ProviderId.Warp => "API key",
        _ => "Key"
    };

    public static string? WorkspacePrompt(ProviderId id) => id is ProviderId.XaiAPI ? "Team ID" : null;

    public static bool UsesRegion(ProviderId id) => id is ProviderId.Moonshot or ProviderId.Zai;
}

/// <summary>
/// What a reading was fetched for. Placement and overlay-visibility-per-provider are left out, so a
/// settings change that only moves the rail is not mistaken for one that invalidates the numbers.
/// </summary>
public readonly record struct FetchInputs(bool DemoData, IReadOnlyList<ProviderConfiguration> Providers)
{
    public static FetchInputs From(AppPreferences preferences)
    {
        var providers = preferences.Providers
            .Where(provider => provider.Enabled && CanFetch(provider))
            .Select(provider => provider with { ShowInOverlay = true })
            .OrderBy(provider => provider.Id)
            .ToList();
        return new(preferences.DemoData, providers);
    }

    /// <summary>
    /// Whether a reading could even be attempted. A Custom JSON connector throws on an endpoint or
    /// dashboard URL the policy rejects, so a half-typed one keeps its own provider out of the
    /// comparison — and out of the refetch decision — without freezing every other provider too.
    /// </summary>
    public static bool CanFetch(ProviderConfiguration provider)
    {
        if (provider.Mode != ProviderMode.CustomJson) return true;
        var custom = provider.CustomValue;
        return Usable(custom.Endpoint)
               && (string.IsNullOrWhiteSpace(custom.DashboardUrl) || Usable(custom.DashboardUrl));
    }

    private static bool Usable(string url)
    {
        try { EndpointPolicy.Validate(url); return true; }
        catch (UsageMeterException) { return false; }
    }

    /// <remarks>
    /// <see cref="Providers"/> is null on <c>default(FetchInputs)</c>, which is what a host holds
    /// before its first reading. Comparing against that must answer "different", not throw.
    /// </remarks>
    public bool Equals(FetchInputs other)
        => DemoData == other.DemoData
           && (Providers ?? []).SequenceEqual(other.Providers ?? []);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DemoData);
        foreach (var provider in Providers ?? []) hash.Add(provider);
        return hash.ToHashCode();
    }
}
