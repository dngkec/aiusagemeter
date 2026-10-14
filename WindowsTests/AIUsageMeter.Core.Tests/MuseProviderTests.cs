using System.Text.Json;
using AIUsageMeter.Core;

namespace AIUsageMeter.Core.Tests;

/// <summary>
/// Meta Muse is a catalog provider on both platforms. Meta has not published a
/// documented usage/billing endpoint, so live fetch must stay on Custom JSON /
/// Manual budget rather than inventing a private API.
/// </summary>
[TestClass]
public sealed class MuseProviderTests
{
    [TestMethod]
    public void MuseIsInTheSharedCatalogWithStableIdentity()
    {
        Assert.IsTrue(ProviderInfo.All.Contains(ProviderId.Muse));
        Assert.AreEqual("Meta Muse", ProviderId.Muse.DisplayName());
        Assert.AreEqual("Mu", ProviderId.Muse.Monogram());
    }

    [TestMethod]
    public void MigratedMuseAtTheEndOfTheListStillHasRailAndSidebarClearance()
    {
        // Upgrades append Muse after Custom. The rail formula and sidebar clearance must keep it visible.
        var migrated = PreferencesMigration.Migrate(new AppPreferences(
            1,
            ProviderInfo.All.Where(id => id != ProviderId.Muse).Select(id => new ProviderConfiguration(id)).ToList()));
        Assert.AreEqual(ProviderId.Muse, migrated.Providers[^1].Id);

        const double item = 46 + 8 + 19;
        const double railPadding = 18;
        const double supportBlock = 11 + 1 + 22;
        var enabledCount = migrated.Providers.Count; // worst case: every catalog row on the rail
        Assert.IsTrue(OverlayLayout.GaugesClearSupport(enabledCount, item, 6, railPadding, supportBlock));
    }

    [TestMethod]
    public void MuseSerializesAsCamelCaseMuseForCrossPlatformPreferences()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        Assert.AreEqual("\"muse\"", JsonSerializer.Serialize(ProviderId.Muse, options));
        Assert.AreEqual(ProviderId.Muse, JsonSerializer.Deserialize<ProviderId>("\"muse\"", options));
    }

    [TestMethod]
    public void MuseNeedsNoAppOwnedSecretOrMonthlyBudgetField()
    {
        Assert.IsNull(SecretAccounts.For(ProviderId.Muse));
        Assert.IsFalse(LiveCredential.UsesMonthlyBudget(ProviderId.Muse));
        Assert.IsFalse(LiveCredential.UsesRegion(ProviderId.Muse));
        Assert.IsNull(LiveCredential.WorkspacePrompt(ProviderId.Muse));
    }

    [TestMethod]
    public void PreferencesMigrationInsertsMuseOnBothPlatformsCatalog()
    {
        var withoutMuse = new AppPreferences(
            1,
            ProviderInfo.All.Where(id => id != ProviderId.Muse).Select(id => new ProviderConfiguration(id)).ToList());
        Assert.IsFalse(withoutMuse.Providers.Any(x => x.Id == ProviderId.Muse));

        var migrated = PreferencesMigration.Migrate(withoutMuse);
        Assert.IsTrue(migrated.Providers.Any(x => x.Id == ProviderId.Muse));
        Assert.AreEqual(ProviderInfo.All.Length, migrated.Providers.Count);
        Assert.IsFalse(migrated.Providers.Single(x => x.Id == ProviderId.Muse).Enabled);
    }

    [TestMethod]
    public async Task LiveMuseFetchReportsSetupNeededRatherThanInventingAnEndpoint()
    {
        var secrets = new MemorySecrets();
        var credentials = new MissingCredentials();
        var service = new ProviderService(new ProviderContext(new RejectingHttp(), secrets, credentials));
        var error = await Assert.ThrowsExactlyAsync<UsageMeterException>(() =>
            service.FetchAsync(new ProviderConfiguration(ProviderId.Muse, Enabled: true, Mode: ProviderMode.Live), default));
        Assert.AreEqual(UsageErrorKind.SetupNeeded, error.Kind);
        StringAssert.Contains(error.Message, "No safe built-in usage endpoint");
    }

    [TestMethod]
    public async Task ManualAndCustomModesRemainAvailableForMuse()
    {
        var secrets = new MemorySecrets();
        var credentials = new MissingCredentials();
        var service = new ProviderService(new ProviderContext(new RejectingHttp(), secrets, credentials));

        var manual = await service.FetchAsync(new ProviderConfiguration(
            ProviderId.Muse, Enabled: true, Mode: ProviderMode.Manual,
            Manual: new ManualBudget(12, 40)), default);
        Assert.AreEqual(DataSourceKind.Manual, manual.Source);
        Assert.AreEqual(30d, manual.Windows[0].Percent);

        secrets.Write("custom.Muse", "unused");
        var http = new FixedJsonHttp("""{"usage":{"percent":55,"used":55,"limit":100}}""");
        var customService = new ProviderService(new ProviderContext(http, secrets, credentials));
        var custom = await customService.FetchAsync(new ProviderConfiguration(
            ProviderId.Muse, Enabled: true, Mode: ProviderMode.CustomJson,
            Custom: new CustomConnector(Endpoint: "https://example.com/usage", PercentPath: "usage.percent")), default);
        Assert.AreEqual(DataSourceKind.CustomJson, custom.Source);
        Assert.AreEqual(55d, custom.Windows[0].Percent, 0.001);
    }

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public string? Read(string account) => _values.TryGetValue(account, out var value) ? value : null;
        public void Write(string account, string? value)
        {
            if (string.IsNullOrEmpty(value)) _values.Remove(account);
            else _values[account] = value;
        }
    }

    private sealed class MissingCredentials : ICredentialDiscovery
    {
        public DiscoveredCredential ReadJson(params string[] relativeCandidates)
            => throw new UsageMeterException("No credential.", UsageErrorKind.SetupNeeded);
        public string ReadCopilotToken() => throw new UsageMeterException("No credential.", UsageErrorKind.SetupNeeded);
    }

    private sealed class RejectingHttp : IUsageHttpClient
    {
        public Task<byte[]> SendAsync(HttpRequestMessage request, int maximumBytes, CancellationToken cancellationToken)
            => throw new InvalidOperationException("HTTP must not be used for Muse live mode.");
    }

    private sealed class FixedJsonHttp(string json) : IUsageHttpClient
    {
        public Task<byte[]> SendAsync(HttpRequestMessage request, int maximumBytes, CancellationToken cancellationToken)
            => Task.FromResult(System.Text.Encoding.UTF8.GetBytes(json));
    }
}
