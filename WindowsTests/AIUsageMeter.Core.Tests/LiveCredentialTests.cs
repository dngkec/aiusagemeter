using AIUsageMeter.Core;

namespace AIUsageMeter.Core.Tests;

[TestClass]
public sealed class LiveCredentialTests
{
    [TestMethod]
    public void XaiAsksForATeamIdAndAManagementKey()
    {
        Assert.AreEqual("Team ID", LiveCredential.WorkspacePrompt(ProviderId.XaiAPI));
        Assert.AreEqual("Management key", LiveCredential.Prompt(ProviderId.XaiAPI));
        Assert.IsTrue(LiveCredential.UsesMonthlyBudget(ProviderId.XaiAPI));
        Assert.IsFalse(LiveCredential.UsesRegion(ProviderId.XaiAPI));
    }

    [TestMethod]
    public void MoonshotAndZaiAskForARegion()
    {
        Assert.IsTrue(LiveCredential.UsesRegion(ProviderId.Moonshot));
        Assert.IsTrue(LiveCredential.UsesRegion(ProviderId.Zai));
        Assert.IsFalse(LiveCredential.UsesRegion(ProviderId.Claude));
    }

    [TestMethod]
    public void ClaudeLiveHasNoAppOwnedSecretField()
    {
        Assert.IsNull(SecretAccounts.For(ProviderId.Claude));
        Assert.IsFalse(LiveCredential.UsesMonthlyBudget(ProviderId.Claude));
        Assert.IsNull(LiveCredential.WorkspacePrompt(ProviderId.Claude));
    }

    [TestMethod]
    public void AnthropicCostUsesAnAdminKeyAndAMonthlyBudget()
    {
        Assert.AreEqual("anthropic.adminKey", SecretAccounts.For(ProviderId.AnthropicCost));
        Assert.AreEqual("Admin key", LiveCredential.Prompt(ProviderId.AnthropicCost));
        Assert.IsTrue(LiveCredential.UsesMonthlyBudget(ProviderId.AnthropicCost));
    }
}

[TestClass]
public sealed class FetchInputsTests
{
    [TestMethod]
    public void DisabledProvidersAreDropped()
    {
        var preferences = AppPreferences.Defaults with
        {
            Providers =
            [
                new(ProviderId.Claude, Enabled: true),
                new(ProviderId.Codex, Enabled: false)
            ]
        };

        var inputs = FetchInputs.From(preferences);
        Assert.AreEqual(1, inputs.Providers.Count);
        Assert.AreEqual(ProviderId.Claude, inputs.Providers[0].Id);
    }

    [TestMethod]
    public void ShowInOverlayDoesNotChangeTheFingerprint()
    {
        var shown = AppPreferences.Defaults with
        {
            Providers = [new(ProviderId.Claude, Enabled: true, ShowInOverlay: true)]
        };
        var hidden = shown with
        {
            Providers = [new(ProviderId.Claude, Enabled: true, ShowInOverlay: false)]
        };

        Assert.AreEqual(FetchInputs.From(shown), FetchInputs.From(hidden));
    }

    [TestMethod]
    public void RailOrderDoesNotChangeTheFingerprint()
    {
        var a = AppPreferences.Defaults with
        {
            Providers =
            [
                new(ProviderId.Claude, Enabled: true),
                new(ProviderId.Codex, Enabled: true)
            ]
        };
        var b = a with
        {
            Providers =
            [
                new(ProviderId.Codex, Enabled: true),
                new(ProviderId.Claude, Enabled: true)
            ]
        };

        Assert.AreEqual(FetchInputs.From(a), FetchInputs.From(b));
    }

    [TestMethod]
    public void EnablingAProviderChangesTheFingerprint()
    {
        var off = AppPreferences.Defaults with
        {
            Providers = [new(ProviderId.Claude, Enabled: false)]
        };
        var on = off with
        {
            Providers = [new(ProviderId.Claude, Enabled: true)]
        };

        Assert.AreNotEqual(FetchInputs.From(off), FetchInputs.From(on));
    }

    [TestMethod]
    public void DemoDataChangesTheFingerprint()
    {
        var live = AppPreferences.Defaults with { DemoData = false, Providers = [new(ProviderId.Claude, Enabled: true)] };
        var demo = live with { DemoData = true };
        Assert.AreNotEqual(FetchInputs.From(live), FetchInputs.From(demo));
    }
}

[TestClass]
public sealed class FetchInputsDefaultTests
{
    [TestMethod]
    public void ComparingAgainstAnUninitialisedValueAnswersDifferentRatherThanThrowing()
    {
        var preferences = AppPreferences.Defaults with
        {
            Providers = [new ProviderConfiguration(ProviderId.Claude, Enabled: true)]
        };
        var inputs = FetchInputs.From(preferences);

        // A host holds default(FetchInputs) until its first reading lands. Its provider list is
        // null, and comparing against it used to throw ArgumentNullException from SequenceEqual.
        Assert.IsTrue(inputs != default);
        Assert.IsTrue(default(FetchInputs) != inputs);
        Assert.IsTrue(default(FetchInputs) == default);
        Assert.AreEqual(default(FetchInputs).GetHashCode(), default(FetchInputs).GetHashCode());
    }
}
