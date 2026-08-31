using System.Net;
using System.Text;
using AIUsageMeter.Core;

namespace AIUsageMeter.Core.Tests;

[TestClass]
public sealed class SecurityAndCoordinatorTests
{
    [TestMethod]
    public void EndpointPolicyAllowsHttpsAndLoopbackOnly()
    {
        Assert.AreEqual("https", EndpointPolicy.Validate("https://example.com/usage").Scheme);
        Assert.AreEqual("http", EndpointPolicy.Validate("http://localhost:11434/usage").Scheme);
        Assert.AreEqual("http", EndpointPolicy.Validate("http://[::1]:11434/usage").Scheme);
        Assert.ThrowsExactly<UsageMeterException>(() => EndpointPolicy.Validate("http://example.com/usage"));
        Assert.ThrowsExactly<UsageMeterException>(() => EndpointPolicy.Validate("https://key@example.com/usage"));
    }

    [TestMethod]
    public async Task BoundedClientRejectsAnnouncedAndStreamingOversizeBodies()
    {
        using var announced = new BoundedHttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(new byte[11]) }));
        await Assert.ThrowsExactlyAsync<UsageMeterException>(() => announced.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.com"), 10, default));

        using var streaming = new BoundedHttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(new MemoryStream(new byte[11])) }));
        await Assert.ThrowsExactlyAsync<UsageMeterException>(() => streaming.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.com"), 10, default));
    }

    [TestMethod]
    public void MigrationAddsProvidersDeduplicatesAndClamps()
    {
        var input = new AppPreferences(1, [new(ProviderId.Codex), new(ProviderId.Codex)], 2, VerticalOffset: 9_000);
        var migrated = PreferencesMigration.Migrate(input);
        Assert.AreEqual(ProviderInfo.All.Length, migrated.Providers.Count);
        Assert.AreEqual(30d, migrated.RefreshIntervalSeconds);
        Assert.AreEqual(2_000d, migrated.VerticalOffset);
    }

    [TestMethod]
    public void OverlayPlacementPinsToRightAndClampsVerticalOffset()
    {
        var frame = OverlayLayout.Place(new MeterRect(100, 50, 1200, 800), 380, 300, VerticalPosition.Bottom, 900);
        Assert.AreEqual(920d, frame.X);
        Assert.AreEqual(550d, frame.Y);
    }

    [TestMethod]
    public void DemoDataIsDeterministicInShapeAndClearlyLabelled()
    {
        var snapshots = DemoData.Snapshots([ProviderId.Claude, ProviderId.Codex]);
        Assert.HasCount(2, snapshots);
        Assert.IsTrue(snapshots.All(x => x.Source == DataSourceKind.Demo && x.Message == "DEMO DATA"));
    }

    [TestMethod]
    public async Task RefreshCoordinatorIsolatesProviderFailures()
    {
        var preferences = AppPreferences.Defaults with
        {
            Providers = [new(ProviderId.Claude, Enabled: true), new(ProviderId.Codex, Enabled: true)]
        };
        var coordinator = new RefreshCoordinator(new StubProviderFetcher());
        var result = await coordinator.RefreshAsync(preferences, default);
        Assert.HasCount(2, result);
        Assert.AreEqual(ProviderStatus.Offline, result.Single(x => x.Id == ProviderId.Claude).Status);
        Assert.AreEqual(ProviderStatus.Ready, result.Single(x => x.Id == ProviderId.Codex).Status);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class StubProviderFetcher : IProviderFetcher
    {
        public Task<ProviderSnapshot> FetchAsync(ProviderConfiguration configuration, CancellationToken cancellationToken)
        {
            if (configuration.Id == ProviderId.Claude) throw new UsageMeterException("Offline", UsageErrorKind.Offline);
            return Task.FromResult(new ProviderSnapshot(configuration.Id, [new UsageWindow("x", "X", 10, 100)]));
        }
    }
}
