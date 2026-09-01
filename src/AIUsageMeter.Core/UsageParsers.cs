using System.Text.Json.Nodes;

namespace AIUsageMeter.Core;

public static class UsageParsers
{
    public static IReadOnlyList<UsageWindow> Claude(ReadOnlyMemory<byte> data)
    {
        var root = Json.Parse(data);
        var output = new List<UsageWindow>();
        if (root.At("limits") is JsonArray limits)
        {
            for (var index = 0; index < limits.Count; index++)
            {
                var item = limits[index];
                var percent = item.Number("percent", "utilization", "used_percent");
                if (percent is null) continue;
                var id = item.Text("kind") ?? $"limit-{index}";
                var label = id switch { "session" => "Current session", "weekly_all" => "All models", _ => item.Text("scope.model.display_name") ?? Title(id) };
                if (id is "extra_usage" or "extra" or "overage" or "usage_credits") label = "Extra usage";
                output.Add(new(id, label, percent.Value, 100, item.Date("resets_at", "reset_at")));
            }
        }
        if (output.Count == 0)
        {
            AddPercent(output, root, "session", "Current session", ["five_hour.utilization", "fiveHour.utilization", "session.utilization"], ["five_hour.resets_at", "fiveHour.resetsAt", "session.resets_at"]);
            AddPercent(output, root, "weekly", "All models", ["seven_day.utilization", "sevenDay.utilization", "weekly.utilization"], ["seven_day.resets_at", "sevenDay.resetsAt", "weekly.resets_at"]);
            AddPercent(output, root, "opus", "Opus", ["seven_day_opus.utilization", "opus.utilization"], ["seven_day_opus.resets_at", "opus.resets_at"]);
            AddPercent(output, root, "sonnet", "Sonnet", ["seven_day_sonnet.utilization", "sonnet.utilization"], ["seven_day_sonnet.resets_at", "sonnet.resets_at"]);
        }
        MergeExtras(output, root);
        return Required(output);
    }

    public static IReadOnlyList<UsageWindow> Codex(ReadOnlyMemory<byte> data)
    {
        var root = Json.Parse(data);
        var output = new List<UsageWindow>();
        AddPercent(output, root, "primary", "Current session",
            ["rate_limit.primary_window.used_percent", "rate_limit.primary.used_percent", "rate_limit.primaryWindow.usedPercent", "primary.used_percent"],
            ["rate_limit.primary_window.resets_at", "rate_limit.primary.resets_at", "rate_limit.primary_window.reset_at", "primary.resets_at"]);
        AddPercent(output, root, "secondary", "Weekly",
            ["rate_limit.secondary_window.used_percent", "rate_limit.secondary.used_percent", "rate_limit.secondaryWindow.usedPercent", "secondary.used_percent"],
            ["rate_limit.secondary_window.resets_at", "rate_limit.secondary.resets_at", "rate_limit.secondary_window.reset_at", "secondary.resets_at"]);
        MergeExtras(output, root);
        return Required(output);
    }

    public static IReadOnlyList<UsageWindow> Cursor(ReadOnlyMemory<byte> data)
    {
        var root = Json.Parse(data);
        var output = new List<UsageWindow>();
        var used = root.Number("individualUsage.plan.used", "planUsage.used", "usage.used", "used", "numRequests");
        var limit = root.Number("individualUsage.plan.limit", "planUsage.limit", "usage.limit", "limit", "maxRequestUsage");
        if (used is not null && limit is not null) output.Add(new("plan", "Plan usage", used.Value, limit.Value, root.Date("billingCycleEnd", "period.billingCycleEnd")));
        var percent = root.Number("usagePercent", "percentUsed", "totalPercentUsed", "planUsage.totalPercentUsed", "individualUsage.plan.percentUsed");
        if (percent is not null && output.Count == 0) output.Add(new("plan", "Plan usage", percent.Value, 100, root.Date("billingCycleEnd", "billingCycleEndMs", "planUsage.billingCycleEnd")));
        if (output.Count == 0)
        {
            var spend = root.Number("totalSpendCents", "planUsage.totalSpendCents", "includedSpendCents");
            var allowance = root.Number("limitCents", "planUsage.limitCents", "includedLimitCents");
            if (spend is not null && allowance is not null) output.Add(new("plan", "Plan usage", spend.Value, allowance.Value, root.Date("billingCycleEnd")));
        }
        if (root.Flag("onDemand.enabled", "onDemandUsage.enabled", "individualUsage.onDemand.enabled") != false)
        {
            used = root.Number("onDemand.used", "onDemandUsage.used", "individualUsage.onDemand.used");
            limit = root.Number("onDemand.limit", "onDemandUsage.limit", "individualUsage.onDemand.limit");
            if (used is not null && limit > 0) output.Add(new("on-demand", "On-demand", used.Value, limit.Value, root.Date("billingCycleEnd"), UsageKind.ExtraUsage));
            else
            {
                used = root.Number("onDemand.usedCents", "onDemandUsage.usedCents", "onDemandSpendCents");
                limit = root.Number("onDemand.limitCents", "onDemandUsage.limitCents", "onDemandLimitCents");
                if (used is not null && limit > 0) output.Add(new("on-demand", "On-demand", used.Value / 100, limit.Value / 100, root.Date("billingCycleEnd"), UsageKind.ExtraUsage));
            }
        }
        MergeExtras(output, root);
        return Required(Deduplicate(output));
    }

    public static IReadOnlyList<UsageWindow> Grok(ReadOnlyMemory<byte>? monthly, ReadOnlyMemory<byte>? credits)
    {
        var roots = new List<JsonNode>();
        if (credits is not null) roots.Add(Json.Parse(credits.Value));
        if (monthly is not null) roots.Add(Json.Parse(monthly.Value));
        var output = new List<UsageWindow>();
        foreach (var root in roots)
        {
            var config = root.At("config") ?? root;
            var reset = config.Date("currentPeriod.end", "billingPeriodEnd");
            var percent = config.Number("creditUsagePercent", "usage_percentage", "usagePercent");
            if (percent is null)
            {
                var cap = config.Number("onDemandCap.val", "onDemandCap"); var used = config.Number("onDemandUsed.val", "onDemandUsed");
                if (cap > 0 && used is not null) percent = used / cap * 100;
            }
            if (percent is not null) output.Add(new("weekly", "Weekly", percent.Value, 100, reset));
            if (config.At("productUsage") is JsonArray products)
                for (var index = 0; index < products.Count; index++)
                    if (products[index].Number("usagePercent", "usage_percentage") is { } productPercent)
                    {
                        var product = products[index].Text("product") ?? $"product-{index}";
                        output.Add(new(product, products[index].Text("product") ?? "Product", productPercent, 100, reset));
                    }
            var limit = root.Number("monthlyLimit", "billing.monthlyLimit", "config.monthlyLimit"); var monthlyUsed = root.Number("used", "monthlyUsed", "billing.used", "config.used");
            if (limit > 0 && monthlyUsed is not null) output.Add(new("monthly", "Monthly", monthlyUsed.Value, limit.Value, root.Date("billingPeriodEnd", "billing.billingPeriodEnd")));
            var remaining = root.Number("remainingCredits", "credits.remaining", "balance"); var total = root.Number("totalCredits", "credits.total", "creditLimit");
            if (remaining is not null && total > 0) output.Add(new("credits", "Credits", Math.Max(0, total.Value - remaining.Value), total.Value, root.Date("billingPeriodEnd", "credits.expiresAt"), UsageKind.Credits));
        }
        return Required(Deduplicate(output));
    }

    public static IReadOnlyList<UsageWindow> Copilot(ReadOnlyMemory<byte> data)
    {
        var root = Json.Parse(data);
        var output = new List<UsageWindow>();
        var reset = root.Date("quota_reset_date_utc", "quota_reset_date");
        if (root.At("quota_snapshots") is JsonObject snapshots)
        {
            var preference = new[] { "premium_interactions", "chat", "completions" };
            var keys = preference.Concat(snapshots.Select(x => x.Key).Where(x => !preference.Contains(x)).Order()).ToList();
            foreach (var key in keys)
            {
                if (!snapshots.TryGetPropertyValue(key, out var item) || item is null || item.Flag("unlimited") == true) continue;
                var extra = key.Contains("extra", StringComparison.OrdinalIgnoreCase) || key.Contains("credit", StringComparison.OrdinalIgnoreCase) || key.Contains("overage", StringComparison.OrdinalIgnoreCase);
                var label = extra ? "Credits" : Title(key);
                var remainingPercent = item.Number("percent_remaining");
                if (remainingPercent is not null) output.Add(new(key, label, Math.Max(0, 100 - remainingPercent.Value), 100, reset, extra ? UsageKind.Quota : UsageKind.Quota));
                else
                {
                    var total = item.Number("quota_entitlement", "entitlement", "limit", "total");
                    var used = item.Number("used");
                    var remaining = item.Number("quota_remaining", "remaining");
                    if (total is not null && (used is not null || remaining is not null))
                        output.Add(new(key, label, used ?? Math.Max(0, total.Value - remaining!.Value), total.Value, reset, extra ? UsageKind.Credits : UsageKind.Quota));
                }
            }
        }
        MergeExtras(output, root);
        return Required(output);
    }

    public static IReadOnlyList<UsageWindow> Gemini(ReadOnlyMemory<byte> data)
    {
        var root = Json.Parse(data);
        var buckets = root.At("buckets") as JsonArray ?? root.At("quota.buckets") as JsonArray ?? [];
        var output = new List<UsageWindow>();
        for (var index = 0; index < buckets.Count; index++)
        {
            var item = buckets[index];
            var remaining = item.Number("remainingAmount", "remaining", "remainingFraction");
            var limit = item.Number("limit", "total", "maxAmount") ?? (remaining <= 1 ? 1 : null);
            if (remaining is null || limit is null || limit <= 0) continue;
            var used = remaining <= 1 && limit == 1 ? 1 - remaining.Value : Math.Max(0, limit.Value - remaining.Value);
            var id = item.Text("modelId") ?? $"bucket-{index}";
            output.Add(new(id, item.Text("displayName") ?? item.Text("modelId") ?? "Model quota", used, limit.Value, item.Date("resetTime", "resetAt")));
        }
        MergeExtras(output, root);
        return Required(output);
    }

    public static IReadOnlyList<UsageWindow> Kimi(ReadOnlyMemory<byte> data)
    {
        var root = Json.Parse(data);
        var container = (root.At("usages") as JsonArray)?.FirstOrDefault() ?? root.At("data") ?? root;
        var output = new List<UsageWindow>();
        var pool = container.At("usage") ?? container.At("detail") ?? root.At("usage");
        var counted = Counted(pool, "requests", "Requests", UsageKind.Credits);
        if (counted is not null) output.Add(counted);
        var limits = container.At("limits") as JsonArray ?? root.At("limits") as JsonArray ?? [];
        for (var index = 0; index < limits.Count; index++)
        {
            var entry = limits[index];
            counted = Counted(entry.At("detail") ?? entry, $"limit-{index}", RateLabel(entry.At("window")), UsageKind.Quota);
            if (counted is not null) output.Add(counted);
        }
        MergeExtras(output, root);
        return Required(output);
    }

    public static IReadOnlyList<UsageWindow> AnthropicCost(ReadOnlyMemory<byte> data, double budget)
    {
        var root = Json.Parse(data);
        var cents = 0d;
        var found = false;
        foreach (var bucket in root.At("data").Array())
            foreach (var item in bucket.At("results").Array())
                if (item.Number("amount", "cost") is { } amount) { cents += amount; found = true; }
        if (!found && root.Number("total_cost", "amount", "cost") is { } total) { cents = total; found = true; }
        if (!found) return Required([]);
        return [new("monthly-cost", "Monthly API cost", cents / 100, Math.Max(0.01, budget), null, UsageKind.ApiCost)];
    }

    public static IReadOnlyList<UsageWindow> OpenAICost(ReadOnlyMemory<byte> data, double budget)
    {
        var root = Json.Parse(data);
        var total = 0d;
        var found = false;
        foreach (var bucket in root.At("data").Array())
            foreach (var item in (bucket.At("results") ?? bucket.At("result")).Array())
                if (item.Number("amount.value", "amount", "cost", "usd") is { } amount) { total += amount; found = true; }
        if (!found && root.Number("total_cost", "amount", "cost", "total_usage") is { } fallbackAmount) { total = fallbackAmount; found = true; }
        if (!found) return Required([]);
        return [new("monthly-cost", "Monthly API cost", total, Math.Max(0.01, budget), null, UsageKind.ApiCost)];
    }

    public static IReadOnlyList<UsageWindow> OpenRouter(ReadOnlyMemory<byte>? credits, ReadOnlyMemory<byte>? key, double budget)
    {
        var output = new List<UsageWindow>();
        if (credits is not null)
        {
            var root = Json.Parse(credits.Value); var container = root.At("data") ?? root;
            var total = container.Number("total_credits", "totalCredits"); var used = container.Number("total_usage", "totalUsage", "usage");
            if (total > 0 && used is not null) output.Add(new("credits", "Credits", used.Value, total.Value, null, UsageKind.Credits));
        }
        if (key is not null)
        {
            var root = Json.Parse(key.Value); var container = root.At("data") ?? root; var limit = container.Number("limit");
            if (limit > 0)
            {
                var remaining = container.Number("limit_remaining", "limitRemaining");
                var used = remaining is null ? container.Number("usage_monthly", "usage") ?? 0 : Math.Max(0, limit.Value - remaining.Value);
                output.Add(new("key-limit", "Key limit", used, limit.Value, null, UsageKind.ApiCost));
            }
            if (output.Count == 0 && container.Number("usage_monthly", "usageMonthly") is { } monthly)
                output.Add(new("monthly", "Monthly API cost", monthly, Math.Max(0.01, budget), null, UsageKind.ApiCost));
        }
        return Required(Deduplicate(output));
    }

    public static IReadOnlyList<UsageWindow> DeepSeek(ReadOnlyMemory<byte> data, double budget)
    {
        var root = Json.Parse(data);
        var infos = (root.At("balance_infos") as JsonArray ?? root.At("balanceInfos") as JsonArray ?? []);
        var chosen = infos.FirstOrDefault(x => string.Equals(x.Text("currency"), "USD", StringComparison.OrdinalIgnoreCase)) ?? infos.FirstOrDefault() ?? root;
        return [Balance(chosen.Number("total_balance", "totalBalance", "balance", "remaining") ?? throw Invalid(), budget)];
    }

    public static IReadOnlyList<UsageWindow> Mistral(ReadOnlyMemory<byte> data, double budget)
    {
        var root = Json.Parse(data); var limits = root.At("limits.completion") ?? root.At("limits") ?? root;
        var used = limits.Number("total_usage", "usage") ?? throw Invalid();
        var cap = limits.Flag("no_monthly_limit") == true ? null : limits.Number("usage_limit");
        return [new("monthly-cost", "Monthly API cost", used, Math.Max(0.01, cap ?? budget), null, UsageKind.ApiCost)];
    }

    public static IReadOnlyList<UsageWindow> XaiBalance(ReadOnlyMemory<byte> data, double budget)
    {
        var cents = Json.Parse(data).Number("total.val", "total") ?? throw Invalid();
        return [Balance(-cents / 100, budget)];
    }

    public static IReadOnlyList<UsageWindow> Moonshot(ReadOnlyMemory<byte> data, double budget)
    {
        var root = Json.Parse(data); var container = root.At("data") ?? root;
        return [Balance(container.Number("available_balance", "availableBalance") ?? throw Invalid(), budget)];
    }

    public static IReadOnlyList<UsageWindow> OpenCode(ReadOnlyMemory<byte> data, DateTimeOffset? now = null)
    {
        var root = Json.Parse(data); var container = root.At("usage") ?? root.At("data") ?? root;
        var output = new List<UsageWindow>();
        foreach (var (id, label, paths) in new[]
        {
            ("rolling", "Session", new[] { "rollingUsage", "rolling", "rolling_usage", "rollingWindow" }),
            ("weekly", "Weekly", new[] { "weeklyUsage", "weekly", "weekly_usage", "weeklyWindow" }),
            ("monthly", "Monthly", new[] { "monthlyUsage", "monthly", "monthly_usage", "monthlyWindow" })
        })
        {
            var lane = paths.Select(container.At).FirstOrDefault(x => x is not null); var percent = lane.Number("usagePercent", "usage_percent", "percentUsed", "used_percent", "percent");
            if (percent is null) continue;
            var seconds = lane.Number("resetInSec", "resetInSeconds", "reset_in_sec");
            output.Add(new(id, label, percent.Value, 100, seconds is null ? null : (now ?? DateTimeOffset.UtcNow).AddSeconds(seconds.Value)));
        }
        return Required(output);
    }

    public static IReadOnlyList<UsageWindow> Zai(ReadOnlyMemory<byte> data)
    {
        var root = Json.Parse(data); var limits = root.At("data.limits") as JsonArray ?? root.At("limits") as JsonArray ?? throw Invalid();
        var output = new List<(double Rank, UsageWindow Window)>();
        for (var index = 0; index < limits.Count; index++)
        {
            var item = limits[index]; var type = item.Text("type") ?? "";
            if (type is not ("TOKENS_LIMIT" or "CREDIT_LIMIT" or "TIME_LIMIT")) continue;
            var cap = item.Number("usage"); var current = item.Number("currentValue"); var remaining = item.Number("remaining");
            var used = new[] { current, cap is not null && remaining is not null ? Math.Max(0, cap.Value - remaining.Value) : null }.Where(x => x is not null).Max();
            var kind = type == "CREDIT_LIMIT" ? UsageKind.Credits : UsageKind.Quota;
            var baseLabel = type switch { "CREDIT_LIMIT" => "Credits", "TIME_LIMIT" => "MCP", _ => "Tokens" };
            var unit = item.Number("unit") is { } unitValue ? (int)unitValue : 0;
            var count = item.Number("number") is { } countValue ? (int)countValue : 0;
            var units = new Dictionary<int, (string Name, double Minutes)> { [1] = ("day", 1440), [3] = ("hour", 60), [5] = ("minute", 1), [6] = ("week", 10080) };
            var label = count > 0 && units.TryGetValue(unit, out var unitInfo) ? $"{baseLabel}, {count} {unitInfo.Name}{(count == 1 ? "" : "s")}" : baseLabel;
            UsageWindow? window = cap > 0 && used is not null
                ? new($"{type.ToLowerInvariant()}-{index}", label, used.Value, cap.Value, item.Date("nextResetTime"), kind)
                : item.Number("percentage") is { } percentage ? new($"{type.ToLowerInvariant()}-{index}", label, percentage, 100, item.Date("nextResetTime")) : null;
            var rank = count > 0 && units.TryGetValue(unit, out unitInfo) ? count * unitInfo.Minutes : double.MaxValue / 2;
            if (window is not null) output.Add((type == "TIME_LIMIT" ? double.MaxValue : rank, window));
        }
        return Required(output.OrderBy(x => x.Rank).Select(x => x.Window).ToList());
    }

    public static IReadOnlyList<UsageWindow> Warp(ReadOnlyMemory<byte> data)
    {
        var root = Json.Parse(data);
        if (root.Text("errors.0.message") is { Length: > 0 } message) throw new UsageMeterException($"Warp rejected the request: {message}", UsageErrorKind.SetupNeeded);
        var info = root.At("data.user.user.requestLimitInfo") ?? root.At("data.user.requestLimitInfo") ?? root.At("requestLimitInfo") ?? throw Invalid();
        if (info.Flag("isUnlimited") == true) throw new UsageMeterException("This Warp plan has no request limit to measure.", UsageErrorKind.SetupNeeded);
        var limit = info.Number("requestLimit"); var used = info.Number("requestsUsedSinceLastRefresh", "requestsUsed");
        if (limit is null or <= 0 || used is null) throw Invalid();
        return [new("requests", "Requests", used.Value, limit.Value, info.Date("nextRefreshTime"), UsageKind.Credits)];
    }

    public static IReadOnlyList<UsageWindow> Custom(ReadOnlyMemory<byte> data, CustomConnector connector)
    {
        var root = Json.Parse(data); var reset = root.Date(connector.ResetPath);
        var haystack = $"{connector.Name} {connector.UsedPath} {connector.LimitPath} {connector.PercentPath}".ToLowerInvariant();
        var kind = haystack.Contains("credit") ? UsageKind.Credits : haystack.Contains("cost") || haystack.Contains("spend") ? UsageKind.ApiCost : UsageKind.Quota;
        var percent = root.Number(connector.PercentPath);
        List<UsageWindow> output;
        if (percent is not null) output = [new("custom", connector.Name, percent.Value, 100, reset, kind)];
        else
        {
            var used = root.Number(connector.UsedPath) ?? throw new UsageMeterException($"Missing field: {connector.UsedPath}", UsageErrorKind.MissingField);
            var limit = root.Number(connector.LimitPath) ?? throw new UsageMeterException($"Missing field: {connector.LimitPath}", UsageErrorKind.MissingField);
            output = [new("custom", connector.Name, used, limit, reset, kind)];
        }
        MergeExtras(output, root);
        return output;
    }

    private static UsageWindow Balance(double remaining, double budget)
    {
        budget = Math.Max(0.01, budget);
        return remaining switch
        {
            <= 0 => new("credits", "Credits", budget, budget, null, UsageKind.ApiCost),
            _ when remaining < budget => new("credits", "Credits", budget - remaining, budget, null, UsageKind.ApiCost),
            _ => new("credits", "Credits", 0, remaining, null, UsageKind.ApiCost)
        };
    }

    private static UsageWindow? Counted(JsonNode? node, string id, string label, UsageKind kind)
    {
        var limit = node.Number("limit", "total", "quota");
        if (limit is null or <= 0) return null;
        var used = node.Number("used", "usage") ?? (node.Number("remaining") is { } remaining ? Math.Max(0, limit.Value - remaining) : null);
        return used is null ? null : new(id, label, used.Value, limit.Value, node.Date("resetTime", "reset_at", "resets_at", "resetAt"), kind);
    }

    private static string RateLabel(JsonNode? node)
    {
        var duration = node.Number("duration"); if (duration is null or <= 0) return "Rate limit";
        var minutes = (node.Text("timeUnit", "time_unit") ?? "").ToUpperInvariant() switch
        { "TIME_UNIT_HOUR" or "HOUR" => duration * 60, "TIME_UNIT_DAY" or "DAY" => duration * 1440, "TIME_UNIT_SECOND" or "SECOND" => duration / 60, _ => duration };
        return minutes >= 1440 ? $"{minutes / 1440:F0}-day limit" : minutes >= 60 ? $"{minutes / 60:F0}-hour limit" : $"{minutes:F0}-minute limit";
    }

    private static void AddPercent(List<UsageWindow> output, JsonNode root, string id, string label, string[] percentPaths, string[] resetPaths)
    {
        if (root.Number(percentPaths) is { } value) output.Add(new(id, label, value, 100, root.Date(resetPaths)));
    }

    private static void MergeExtras(List<UsageWindow> output, JsonNode root)
    {
        var extra = root.At("extra_usage") ?? root.At("extraUsage");
        if (extra is not null && extra.Flag("is_enabled", "enabled") != false)
        {
            var used = extra.Number("used_credits", "usedCredits", "used", "amount"); var limit = extra.Number("monthly_limit", "monthlyLimit", "limit");
            if (used is not null && limit > 0)
            {
                if (limit >= 1_000 && used == Math.Round(used.Value) && limit == Math.Round(limit.Value)) { used /= 100; limit /= 100; }
                AddIfMissing(output, new("extra_usage", "Extra usage", used.Value, limit.Value, null, UsageKind.ExtraUsage));
            }
            else if (extra.Number("utilization", "used_percent") is { } percent) AddIfMissing(output, new("extra_usage", "Extra usage", percent, 100));
        }
        var creditUsed = root.Number("credits.used", "credits.used_amount", "credit.used"); var creditLimit = root.Number("credits.limit", "credits.total", "credit.limit", "credit.total");
        if (creditUsed is not null && creditLimit > 0) AddIfMissing(output, new("credits", "Credits", creditUsed.Value, creditLimit.Value, root.Date("credits.reset_at", "credits.resets_at"), UsageKind.Credits));
        else
        {
            var remaining = root.Number("credits.remaining", "credits.balance", "credit.remaining"); var total = root.Number("credits.total", "credits.limit", "credit.total");
            if (remaining is not null && total > 0) AddIfMissing(output, new("credits", "Credits", Math.Max(0, total.Value - remaining.Value), total.Value, root.Date("credits.reset_at"), UsageKind.Credits));
        }
    }

    private static void AddIfMissing(List<UsageWindow> output, UsageWindow item) { if (output.All(x => x.Id != item.Id)) output.Add(item); }
    private static List<UsageWindow> Deduplicate(IEnumerable<UsageWindow> source) => source.GroupBy(x => x.Id).Select(x => x.First()).ToList();
    private static IReadOnlyList<UsageWindow> Required(List<UsageWindow> output) => output.Count > 0 ? output : throw Invalid();
    private static UsageMeterException Invalid() => new("The provider returned an unsupported response.", UsageErrorKind.InvalidResponse);
    private static string Title(string value) => string.Join(' ', value.Split('_', '-').Select(x => x.Length == 0 ? x : char.ToUpperInvariant(x[0]) + x[1..]));
}
