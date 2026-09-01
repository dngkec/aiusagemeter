namespace AIUsageMeter.Core;

public interface ISecretStore
{
    string? Read(string account);
    void Write(string account, string? value);
}

public interface ICredentialDiscovery
{
    DiscoveredCredential ReadJson(params string[] relativeCandidates);
    string ReadCopilotToken();
}

public sealed record DiscoveredCredential(string SourcePath, IReadOnlyDictionary<string, object?> Values)
{
    public string? String(params string[] paths)
    {
        foreach (var path in paths)
        {
            object? current = Values;
            foreach (var segment in path.Split('.'))
            {
                if (current is IReadOnlyDictionary<string, object?> map && map.TryGetValue(segment, out var child)) current = child;
                else { current = null; break; }
            }
            if (current is string text && !string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }

    public string? RecursiveToken()
    {
        static string? Search(object? node)
        {
            if (node is IReadOnlyDictionary<string, object?> map)
            {
                foreach (var key in new[] { "oauth_token", "access_token", "token", "key" })
                    if (map.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)) return text;
                foreach (var value in map.Values) if (Search(value) is { } found) return found;
            }
            if (node is IEnumerable<object?> list)
                foreach (var value in list) if (Search(value) is { } found) return found;
            return null;
        }
        return Search(Values);
    }
}

public static class SecretAccounts
{
    public static string? For(ProviderId id) => id switch
    {
        ProviderId.AnthropicCost => "anthropic.adminKey", ProviderId.OpenAIAPI => "openai.adminKey",
        ProviderId.OpenRouter => "openrouter.apiKey", ProviderId.DeepSeek => "deepseek.apiKey",
        ProviderId.Mistral => "mistral.adminKey", ProviderId.XaiAPI => "xai.managementKey",
        ProviderId.Moonshot => "moonshot.apiKey", ProviderId.Zai => "zai.apiKey",
        ProviderId.OpenCode => "opencode.apiKey", ProviderId.Warp => "warp.apiKey",
        _ => null
    };
}
