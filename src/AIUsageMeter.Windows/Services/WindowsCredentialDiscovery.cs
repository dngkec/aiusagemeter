using System.Text;
using System.Text.Json;
using System.IO;
using AIUsageMeter.Core;

namespace AIUsageMeter.Windows.Services;

internal sealed class WindowsCredentialDiscovery : ICredentialDiscovery
{
    private const int MaximumCredentialBytes = 2_000_000;
    private readonly string _profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public DiscoveredCredential ReadJson(params string[] relativeCandidates)
    {
        foreach (var relative in relativeCandidates)
        {
            var path = Resolve(relative);
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length is <= 0 or > MaximumCredentialBytes) continue;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
                using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 64, CommentHandling = JsonCommentHandling.Skip });
                if (Convert(document.RootElement) is IReadOnlyDictionary<string, object?> map) return new(path, map);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
        var display = relativeCandidates.FirstOrDefault() ?? "credential file";
        throw new UsageMeterException($"No usable credential was found at {display}.", UsageErrorKind.SetupNeeded);
    }

    public string ReadCopilotToken()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var path in new[]
        {
            Path.Combine(_profile, ".config", "github-copilot", "apps.json"),
            Path.Combine(_profile, ".config", "github-copilot", "hosts.json"),
            Path.Combine(appData, "github-copilot", "apps.json"),
            Path.Combine(appData, "github-copilot", "hosts.json")
        })
        {
            try
            {
                var relative = Path.GetRelativePath(_profile, path);
                var token = ReadJson(relative).RecursiveToken();
                if (!string.IsNullOrWhiteSpace(token)) return token;
            }
            catch (UsageMeterException) { }
        }
        foreach (var path in new[] { Path.Combine(_profile, ".config", "gh", "hosts.yml"), Path.Combine(appData, "GitHub CLI", "hosts.yml") })
        {
            try
            {
                var info = new FileInfo(path); if (!info.Exists || info.Length > MaximumCredentialBytes) continue;
                foreach (var line in File.ReadLines(path, Encoding.UTF8))
                {
                    var bits = line.Split(':', 2, StringSplitOptions.TrimEntries);
                    if (bits.Length == 2 && bits[0] is ("oauth_token" or "oauth-token") && bits[1].Length > 0) return bits[1].Trim('"', '\'');
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        throw new UsageMeterException("No existing GitHub Copilot or GitHub CLI token was found in the Windows profile or roaming AppData.", UsageErrorKind.SetupNeeded);
    }

    private string Resolve(string relative)
    {
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(_profile, normalized));
        var profileRoot = Path.GetFullPath(_profile) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(profileRoot, StringComparison.OrdinalIgnoreCase)) throw new UsageMeterException("Credential path escaped the user profile.", UsageErrorKind.SetupNeeded);
        return combined;
    }

    private static object? Convert(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(x => x.Name, x => Convert(x.Value), StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(Convert).ToList(),
        JsonValueKind.String => element.GetString(), JsonValueKind.Number => element.TryGetInt64(out var value) ? value : element.GetDouble(),
        JsonValueKind.True => true, JsonValueKind.False => false, _ => null
    };
}
