using System.Net;
using System.Net.Http.Headers;

namespace AIUsageMeter.Core;

public static class EndpointPolicy
{
    public static Uri Validate(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            throw new UsageMeterException("Enter an absolute endpoint URL.", UsageErrorKind.InvalidUrl);
        var localHttp = uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri.Host);
        if (uri.Scheme != Uri.UriSchemeHttps && !localHttp)
            throw new UsageMeterException("Endpoints must use HTTPS. HTTP is allowed only for localhost.", UsageErrorKind.InvalidUrl);
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new UsageMeterException("Credentials are not allowed in endpoint URLs.", UsageErrorKind.InvalidUrl);
        return uri;
    }

    private static bool IsLoopback(string host)
    {
        var normalized = host.Trim('[', ']');
        return normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(normalized, out var address) && IPAddress.IsLoopback(address);
    }
}

public interface IUsageHttpClient
{
    Task<byte[]> SendAsync(HttpRequestMessage request, int maximumBytes, CancellationToken cancellationToken);
}

public sealed class BoundedHttpClient : IUsageHttpClient, IDisposable
{
    private readonly HttpClient _client;
    public const int DefaultMaximumBytes = 2_000_000;

    public BoundedHttpClient(HttpMessageHandler? handler = null)
    {
        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 3,
            UseCookies = false
        };
        _client = new HttpClient(handler, true) { Timeout = TimeSpan.FromSeconds(25) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("AIUsageMeter-Windows/1.0");
    }

    public async Task<byte[]> SendAsync(HttpRequestMessage request, int maximumBytes = DefaultMaximumBytes, CancellationToken cancellationToken = default)
    {
        if (maximumBytes is < 1 or > DefaultMaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is 401 or 403) throw new UsageMeterException("Sign-in is required.", UsageErrorKind.Unauthorized);
            if ((int)response.StatusCode == 429) throw new UsageMeterException("The provider is rate limiting requests.", UsageErrorKind.RateLimited);
            if (!response.IsSuccessStatusCode) throw new UsageMeterException($"Provider returned HTTP {(int)response.StatusCode}.", UsageErrorKind.Server);
            if (response.Content.Headers.ContentLength > maximumBytes) throw Oversized();

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0) break;
                if (output.Length + count > maximumBytes) throw Oversized();
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UsageMeterException("The request timed out.", UsageErrorKind.Timeout);
        }
        catch (HttpRequestException)
        {
            throw new UsageMeterException("No network connection.", UsageErrorKind.Offline);
        }
    }

    public void Dispose() => _client.Dispose();
    private static UsageMeterException Oversized() => new("The provider response exceeded the safe size limit.", UsageErrorKind.OversizedResponse);
}

public static class RequestFactory
{
    public static HttpRequestMessage Create(Uri uri, HttpMethod? method = null, string? bearer = null,
        IReadOnlyDictionary<string, string>? headers = null, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, uri) { Content = content };
        if (!string.IsNullOrWhiteSpace(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (headers is not null)
            foreach (var (name, value) in headers)
            {
                if (name.Any(char.IsControl) || value.Any(c => c is '\r' or '\n')) throw new UsageMeterException("A custom header is invalid.", UsageErrorKind.InvalidUrl);
                if (!request.Headers.TryAddWithoutValidation(name, value))
                    throw new UsageMeterException("A custom header is invalid.", UsageErrorKind.InvalidUrl);
            }
        return request;
    }
}
