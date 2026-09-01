namespace AIUsageMeter.Core;

public sealed class RefreshCoordinator(IProviderFetcher providers)
{
    public async Task<IReadOnlyList<ProviderSnapshot>> RefreshAsync(AppPreferences preferences, CancellationToken cancellationToken)
    {
        var enabled = preferences.Providers.Where(x => x.Enabled).ToList();
        if (preferences.DemoData) return DemoData.Snapshots(enabled.Select(x => x.Id));
        var tasks = enabled.Select(x => FetchIsolatedAsync(x, cancellationToken)).ToList();
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<ProviderSnapshot> FetchIsolatedAsync(ProviderConfiguration config, CancellationToken cancellationToken)
    {
        try { return await providers.FetchAsync(config, cancellationToken).ConfigureAwait(false); }
        catch (UsageMeterException error)
        {
            var status = error.Kind switch
            {
                UsageErrorKind.SetupNeeded => ProviderStatus.SetupNeeded, UsageErrorKind.Unauthorized => ProviderStatus.Unauthorized,
                UsageErrorKind.RateLimited => ProviderStatus.RateLimited, UsageErrorKind.Offline or UsageErrorKind.Timeout => ProviderStatus.Offline,
                UsageErrorKind.ExpiredCredential => ProviderStatus.Expired, _ => ProviderStatus.Error
            };
            return new(config.Id, [], status, Message: error.Message, UpdatedAt: DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new(config.Id, [], ProviderStatus.Error, Message: "The provider could not be refreshed.", UpdatedAt: DateTimeOffset.UtcNow); }
    }
}
