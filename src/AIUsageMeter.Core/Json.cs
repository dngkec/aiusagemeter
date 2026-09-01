using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIUsageMeter.Core;

internal static class Json
{
    public static JsonNode Parse(ReadOnlyMemory<byte> data)
    {
        try { return JsonNode.Parse(data.Span) ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
    }

    public static JsonNode? At(this JsonNode? root, string path)
    {
        if (root is null || string.IsNullOrWhiteSpace(path)) return root;
        JsonNode? current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(part, out var next)) current = next;
            else if (current is JsonArray array && int.TryParse(part, out var index) && index >= 0 && index < array.Count) current = array[index];
            else return null;
        }
        return current;
    }

    public static double? Number(this JsonNode? root, params string[] paths)
    {
        foreach (var path in paths)
        {
            var value = root.At(path);
            if (value is null) continue;
            if (value is JsonValue scalar && scalar.TryGetValue<double>(out var number) && double.IsFinite(number)) return number;
            if (value is JsonValue text && text.TryGetValue<string>(out var raw) &&
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number)) return number;
        }
        return null;
    }

    public static string? Text(this JsonNode? root, params string[] paths)
    {
        foreach (var path in paths)
            if (root.At(path) is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        return null;
    }

    public static bool? Flag(this JsonNode? root, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (root.At(path) is not JsonValue value) continue;
            if (value.TryGetValue<bool>(out var flag)) return flag;
            if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out flag)) return flag;
        }
        return null;
    }

    public static DateTimeOffset? Date(this JsonNode? root, params string[] paths)
    {
        foreach (var path in paths)
        {
            var node = root.At(path);
            if (node is null) continue;
            if (node is JsonValue number && number.TryGetValue<long>(out var epoch))
            {
                try { return epoch > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(epoch) : DateTimeOffset.FromUnixTimeSeconds(epoch); }
                catch (ArgumentOutOfRangeException) { continue; }
            }
            if (node is JsonValue text && text.TryGetValue<string>(out var raw))
            {
                if (long.TryParse(raw, CultureInfo.InvariantCulture, out epoch))
                {
                    try { return epoch > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(epoch) : DateTimeOffset.FromUnixTimeSeconds(epoch); }
                    catch (ArgumentOutOfRangeException) { continue; }
                }
                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) return parsed.ToUniversalTime();
                if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            }
        }
        return null;
    }

    public static JsonArray Array(this JsonNode? node) => node as JsonArray ?? [];
    public static JsonObject Object(this JsonNode? node) => node as JsonObject ?? [];
    private static UsageMeterException Invalid() => new("The provider returned an unsupported response.", UsageErrorKind.InvalidResponse);
}
