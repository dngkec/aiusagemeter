using System.Text;
using AIUsageMeter.Core;

namespace AIUsageMeter.Core.Tests;

[TestClass]
public sealed class ModelAndParserTests
{
    private static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);

    [TestMethod]
    public void UsageWindowPreservesOverLimitValuesAndClampsInvalidNumbers()
    {
        Assert.AreEqual(125d, new UsageWindow("x", "X", 125, 100).Percent);
        Assert.AreEqual(0d, new UsageWindow("x", "X", double.NaN, 100).Percent);
        Assert.AreEqual("$4.50 / $10", new UsageWindow("x", "X", 4.5, 10, Kind: UsageKind.ApiCost).ReadingCaption);
    }

    [TestMethod]
    public void ClaudeParsesCurrentAndExtraUsage()
    {
        var windows = UsageParsers.Claude(Json("""
            {"five_hour":{"utilization":42,"resets_at":"2027-01-01T00:00:00Z"},"seven_day":{"utilization":70},"extra_usage":{"is_enabled":true,"used_credits":450,"monthly_limit":1000}}
            """));
        Assert.AreEqual(3, windows.Count);
        Assert.AreEqual(42d, windows[0].Percent);
        Assert.AreEqual(UsageKind.ExtraUsage, windows[2].Kind);
        Assert.AreEqual(4.5d, windows[2].Used);
    }

    [TestMethod]
    public void CodexReadsBothWindowSpellingsAndCredits()
    {
        var windows = UsageParsers.Codex(Json("""
            {"rate_limit":{"primary_window":{"used_percent":52,"resets_at":1800000000},"secondary":{"used_percent":11}},"credits":{"remaining":25,"total":100}}
            """));
        CollectionAssert.AreEqual(new[] { "primary", "secondary", "credits" }, windows.Select(x => x.Id).ToArray());
        Assert.AreEqual(75d, windows[2].Used);
    }

    [TestMethod]
    public void CopilotSkipsUnlimitedQuota()
    {
        var windows = UsageParsers.Copilot(Json("""
            {"quota_reset_date":"2027-03-01","quota_snapshots":{"chat":{"unlimited":true,"percent_remaining":100},"premium_interactions":{"entitlement":300,"quota_remaining":75}}}
            """));
        Assert.HasCount(1, windows);
        Assert.AreEqual(225d, windows[0].Used);
    }

    [TestMethod]
    public void KimiParsesRequestPoolAndRateWindow()
    {
        var windows = UsageParsers.Kimi(Json("""
            {"usages":[{"detail":{"limit":"2048","used":"214"},"limits":[{"window":{"duration":5,"timeUnit":"TIME_UNIT_HOUR"},"detail":{"limit":"200","remaining":"61"}}]}]}
            """));
        Assert.AreEqual("5-hour limit", windows[1].Label);
        Assert.AreEqual(139d, windows[1].Used);
    }

    [TestMethod]
    public void CostAndBalanceParsersKeepCurrencySemantics()
    {
        var anthropic = UsageParsers.AnthropicCost(Json("""{"data":[{"results":[{"amount":"500"},{"amount":"734"}]}]}"""), 100);
        Assert.AreEqual(12.34d, anthropic[0].Used, 0.001);
        var deepSeek = UsageParsers.DeepSeek(Json("""{"balance_infos":[{"currency":"USD","total_balance":"37.66"}]}"""), 50);
        Assert.AreEqual(12.34d, deepSeek[0].Used, 0.001);
    }

    [TestMethod]
    public void CustomConnectorSupportsArrayPathsAndRejectsMissingFields()
    {
        var percent = UsageParsers.Custom(Json("""{"data":[{"pct":"115"}]}"""), new CustomConnector(PercentPath: "data.0.pct"));
        Assert.AreEqual(115d, percent[0].Percent, 0.001);
        Assert.ThrowsExactly<UsageMeterException>(() => UsageParsers.Custom(Json("{}"), new CustomConnector(PercentPath: "missing")));
    }

    [TestMethod]
    public void EveryParserRejectsMalformedJson()
    {
        Assert.ThrowsExactly<UsageMeterException>(() => UsageParsers.Claude(Json("{")));
        Assert.ThrowsExactly<UsageMeterException>(() => UsageParsers.Codex(Json("{")));
        Assert.ThrowsExactly<UsageMeterException>(() => UsageParsers.Gemini(Json("{")));
    }
}
