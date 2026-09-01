using System.Windows;
using AIUsageMeter.Core;
using AIUsageMeter.Windows;
using AIUsageMeter.Windows.Design;

namespace AIUsageMeter.Windows.Tests;

[TestClass]
public sealed class SettingsViewModelTests
{
    [TestMethod]
    public void ChangingOverlayOffsetAppliesWithoutARefetch()
    {
        var (model, host) = Session();
        host.Applies.Clear();
        model.VerticalOffset = 12;
        Assert.HasCount(1, host.Applies);
        Assert.AreEqual(false, host.Applies[0].Refetch);
        Assert.AreEqual(12d, host.Applies[0].Preferences.VerticalOffset);
    }

    [TestMethod]
    public void EnablingAProviderAsksTheHostToDecideRefetch()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: false));
        host.Applies.Clear();
        model.Providers[0].Enabled = true;
        Assert.HasCount(1, host.Applies);
        Assert.IsNull(host.Applies[0].Refetch);
        Assert.IsTrue(host.Applies[0].Preferences.Providers[0].Enabled);
    }

    [TestMethod]
    public void TogglingShowInOverlayStillApplies()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: true, ShowInOverlay: true));
        host.Applies.Clear();
        model.Providers[0].ShowInOverlay = false;
        Assert.HasCount(1, host.Applies);
        Assert.IsFalse(host.Applies[0].Preferences.Providers[0].ShowInOverlay);
        var hidden = FetchInputs.From(host.Applies[0].Preferences);
        model.Providers[0].ShowInOverlay = true;
        Assert.AreEqual(hidden, FetchInputs.From(host.Applies[^1].Preferences));
    }

    [TestMethod]
    public void SearchFiltersByDisplayNameAndBlocksReorder()
    {
        var (model, _) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: true), new ProviderConfiguration(ProviderId.Codex, Enabled: true));
        model.Query = "chatgpt";
        Assert.AreEqual(1, model.FilteredProviders.Count);
        Assert.AreEqual(ProviderId.Codex, model.FilteredProviders[0].Id);
        Assert.IsFalse(model.CanReorder);
        var order = model.Providers[0].Id;
        model.MoveSelected(1);
        Assert.AreEqual(order, model.Providers[0].Id);
    }

    [TestMethod]
    public void MovingAProviderAppliesWithoutAnExplicitRefetch()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: true), new ProviderConfiguration(ProviderId.Codex, Enabled: true));
        model.SelectedProvider = model.Providers[0];
        host.Applies.Clear();
        model.MoveSelected(1);
        Assert.AreEqual(ProviderId.Codex, model.Providers[0].Id);
        Assert.AreEqual(ProviderId.Claude, model.Providers[1].Id);
        Assert.AreEqual(false, host.Applies[0].Refetch);
    }

    [TestMethod]
    public void ManualModeHidesLiveAndCustomSections()
    {
        var (model, _) = Session(new ProviderConfiguration(ProviderId.OpenRouter, Enabled: true, Mode: ProviderMode.Live));
        var item = model.Providers[0];
        Assert.IsTrue(item.ShowLiveSection);
        item.Mode = ProviderMode.Manual;
        Assert.IsTrue(item.ShowManualSection);
        Assert.IsFalse(item.ShowLiveSection);
        Assert.IsFalse(item.ShowCustomSection);
    }

    [TestMethod]
    public void LiveCredentialHelpersDriveProviderFields()
    {
        var (model, _) = Session(
            new ProviderConfiguration(ProviderId.XaiAPI, Enabled: true, Mode: ProviderMode.Live),
            new ProviderConfiguration(ProviderId.Moonshot, Enabled: true, Mode: ProviderMode.Live),
            new ProviderConfiguration(ProviderId.Claude, Enabled: true, Mode: ProviderMode.Live));
        Assert.IsTrue(model.Providers[0].ShowWorkspace);
        Assert.AreEqual("Team ID", model.Providers[0].WorkspacePrompt);
        Assert.IsTrue(model.Providers[1].ShowRegion);
        Assert.IsFalse(model.Providers[2].ShowLiveSecret);
        Assert.IsTrue(model.Providers[2].ShowLocalSignInCopy);
    }

    [TestMethod]
    public void TypingASecretDoesNotWriteTheStoreUntilSave()
    {
        var secrets = new MemorySecretStore();
        var (model, host) = SessionWith(secrets, new ProviderConfiguration(ProviderId.OpenRouter, Enabled: true, Mode: ProviderMode.Live));
        model.SelectedProvider = model.Providers[0];
        model.Providers[0].NewSecret = "sk-test";
        Assert.IsNull(secrets.Read("openrouter.apiKey"));
        model.SaveSecret();
        Assert.AreEqual("sk-test", secrets.Read("openrouter.apiKey"));
        Assert.AreEqual("", model.Providers[0].NewSecret);
        Assert.AreEqual(1, host.RefreshCalls);
    }

    [TestMethod]
    public void ChangingPaneDiscardsATypedSecret()
    {
        var (model, _) = Session(new ProviderConfiguration(ProviderId.OpenRouter, Enabled: true, Mode: ProviderMode.Live));
        model.SelectedProvider = model.Providers[0];
        model.Providers[0].NewSecret = "sk-test";
        model.SelectGeneral();
        Assert.AreEqual("", model.Providers[0].NewSecret);
    }

    [TestMethod]
    public void CustomJsonModeHidesLiveAndManualSections()
    {
        var (model, _) = Session(new ProviderConfiguration(ProviderId.OpenRouter, Enabled: true, Mode: ProviderMode.Live));
        var item = model.Providers[0];
        item.Mode = ProviderMode.CustomJson;
        Assert.IsTrue(item.ShowCustomSection);
        Assert.IsFalse(item.ShowLiveSection);
        Assert.IsFalse(item.ShowManualSection);
    }

    [TestMethod]
    public void RailPositionFollowsOrderAndBlocksTheEnds()
    {
        var (model, _) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: true), new ProviderConfiguration(ProviderId.Codex, Enabled: true));
        Assert.AreEqual("1 of 2", model.Providers[0].RailPosition);
        Assert.AreEqual("2 of 2", model.Providers[1].RailPosition);
        Assert.IsFalse(model.Providers[0].CanMoveUp);
        Assert.IsTrue(model.Providers[0].CanMoveDown);
        model.Query = "c";
        Assert.IsFalse(model.Providers[0].CanMoveUp);
        Assert.IsFalse(model.Providers[0].CanMoveDown);
    }

    [TestMethod]
    public void AnInvalidDashboardUrlWarnsAndLeavesTheReadingsAlone()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Custom, Enabled: true, Mode: ProviderMode.CustomJson));
        var before = FetchInputs.From(model.BuildPreferences());
        host.Applies.Clear();
        model.Providers[0].DashboardUrl = "http://example.com/dash";
        Assert.IsNotNull(model.Providers[0].ConnectorWarning);
        Assert.AreEqual(before, FetchInputs.From(host.Applies[^1].Preferences), "the host has nothing new to fetch");
    }

    [TestMethod]
    public void OverlayOffsetClampsToThreeHundred()
    {
        var (model, host) = Session();
        host.Applies.Clear();
        model.VerticalOffset = 500;
        Assert.AreEqual(300d, model.VerticalOffset);
        Assert.AreEqual(300d, host.Applies[0].Preferences.VerticalOffset);
        Assert.IsTrue(model.OffsetIsNonZero);
        model.ResetOffset();
        Assert.IsFalse(model.OffsetIsNonZero);
    }

    [TestMethod]
    public void SidebarKeysWalkGeneralAboutThenProviders()
    {
        var (model, _) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: true), new ProviderConfiguration(ProviderId.Codex, Enabled: true));
        model.SelectGeneral();
        model.SelectNext();
        Assert.IsTrue(model.IsAboutPane);
        model.SelectNext();
        Assert.AreEqual(ProviderId.Claude, model.SelectedProvider?.Id);
        model.SelectPrevious();
        Assert.IsTrue(model.IsAboutPane);
    }

    [TestMethod]
    public void RemoveClearsTheStoredSecretAndRefreshes()
    {
        var secrets = new MemorySecretStore();
        secrets.Write("openrouter.apiKey", "sk-old");
        var (model, host) = SessionWith(secrets, new ProviderConfiguration(ProviderId.OpenRouter, Enabled: true, Mode: ProviderMode.Live));
        model.SelectedProvider = model.Providers[0];
        Assert.IsTrue(model.Providers[0].HasStoredSecret);
        model.RemoveSecret();
        Assert.IsNull(secrets.Read("openrouter.apiKey"));
        Assert.IsFalse(model.Providers[0].HasStoredSecret);
        Assert.AreEqual(1, host.RefreshCalls);
    }

    [TestMethod]
    public void AnInvalidCustomEndpointWarnsAndLeavesTheReadingsAlone()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Custom, Enabled: true, Mode: ProviderMode.CustomJson));
        var before = FetchInputs.From(model.BuildPreferences());
        host.Applies.Clear();
        model.Providers[0].Endpoint = "http://example.com/usage";
        Assert.IsNotNull(model.Providers[0].ConnectorWarning);
        Assert.AreEqual(before, FetchInputs.From(host.Applies[^1].Preferences));
    }

    [TestMethod]
    public void OneBrokenConnectorDoesNotFreezeEveryOtherProvider()
    {
        // The old rule suppressed refetch for the whole window while any custom URL was unusable,
        // so a half-typed endpoint on one provider silently stopped readings for all of them.
        var (model, host) = Session(
            new ProviderConfiguration(ProviderId.Custom, Enabled: true, Mode: ProviderMode.CustomJson),
            new ProviderConfiguration(ProviderId.OpenRouter, Enabled: true, Mode: ProviderMode.Live));
        model.Providers[0].Endpoint = "http://example.com/usage";
        var stale = FetchInputs.From(model.BuildPreferences());

        host.Applies.Clear();
        model.Providers[1].MonthlyBudget = 250;
        Assert.IsNull(host.Applies[^1].Refetch, "the host decides");
        Assert.AreNotEqual(stale, FetchInputs.From(host.Applies[^1].Preferences), "the healthy provider still owes a reading");
    }

    [TestMethod]
    public void FixingAConnectorPutsItsProviderBackInTheFetch()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Custom, Enabled: true, Mode: ProviderMode.CustomJson));
        model.Providers[0].Endpoint = "http://example.com/usage";
        var broken = FetchInputs.From(model.BuildPreferences());

        host.Applies.Clear();
        model.Providers[0].Endpoint = "https://example.com/usage";
        Assert.IsNull(model.Providers[0].ConnectorWarning);
        Assert.AreNotEqual(broken, FetchInputs.From(host.Applies[^1].Preferences));
    }

    [TestMethod]
    public void PersistErrorSurfacesAsAFailureNotice()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: true));
        host.PersistError = "Could not save.";
        host.RaiseChanged();
        Assert.IsNotNull(model.Notice);
        Assert.IsTrue(model.Notice!.IsFailure);
    }

    [TestMethod]
    public void FilteringTheSidebarKeepsTheSelectedProvider()
    {
        // The sidebar binds SelectedItem two-way. Refilling the filtered list used to reset the
        // ListBox's selection, which wrote null back and blanked the detail pane mid-keystroke.
        var report = Rendering.Sta(() =>
        {
            var (model, _) = Session(
                new ProviderConfiguration(ProviderId.Claude, Enabled: true),
                new ProviderConfiguration(ProviderId.Codex, Enabled: true));
            _ = Sidebar(model);
            var before = model.SelectedProvider?.Id;
            model.Query = "cl";
            var whileFiltered = model.SelectedProvider?.Id;
            model.Query = "codex";
            var whenFilteredOut = model.SelectedProvider?.Id;
            return (before, whileFiltered, whenFilteredOut, model.IsProviderPane);
        });

        Assert.AreEqual(ProviderId.Claude, report.before);
        Assert.AreEqual(ProviderId.Claude, report.whileFiltered);
        Assert.AreEqual(ProviderId.Claude, report.whenFilteredOut, "a hidden row must not blank the pane");
        Assert.IsTrue(report.IsProviderPane);
    }

    [TestMethod]
    public void ReorderingKeepsTheSidebarSelection()
    {
        var selected = Rendering.Sta(() =>
        {
            var (model, _) = Session(
                new ProviderConfiguration(ProviderId.Claude, Enabled: true),
                new ProviderConfiguration(ProviderId.Codex, Enabled: true));
            _ = Sidebar(model);
            model.MoveSelected(1);
            return model.SelectedProvider?.Id;
        });
        Assert.AreEqual(ProviderId.Claude, selected);
    }

    [TestMethod]
    public void ReassigningTheSameProviderKeepsItSelected()
    {
        var (model, _) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: true));
        var item = model.Providers[0];
        model.SelectedProvider = item;
        model.SelectedProvider = item;
        Assert.IsTrue(item.IsSelected);
        Assert.AreSame(item, model.SelectedProvider);
    }

    [TestMethod]
    public void ATypedSecretDoesNotFollowTheUserToAnotherProvider()
    {
        var (model, _) = Session(
            new ProviderConfiguration(ProviderId.OpenRouter, Enabled: true, Mode: ProviderMode.Live),
            new ProviderConfiguration(ProviderId.DeepSeek, Enabled: true, Mode: ProviderMode.Live));
        model.SelectedProvider = model.Providers[0];
        model.Providers[0].NewSecret = "sk-half-typed";
        model.SelectedProvider = model.Providers[1];
        Assert.AreEqual("", model.Providers[0].NewSecret);
        Assert.AreEqual("", model.Providers[1].NewSecret);
    }

    [TestMethod]
    public void SwitchingModeRereadsWhetherASecretIsStored()
    {
        // Live and Custom JSON keep separate accounts, so "a secret is saved" is mode-specific.
        var secrets = new MemorySecretStore();
        secrets.Write("openrouter.apiKey", "sk-live");
        var (model, _) = SessionWith(secrets, new ProviderConfiguration(ProviderId.OpenRouter, Enabled: true, Mode: ProviderMode.Live));
        var item = model.Providers[0];
        Assert.IsTrue(item.HasStoredSecret);

        item.Mode = ProviderMode.CustomJson;
        Assert.AreEqual("custom.OpenRouter", item.SecretAccount);
        Assert.IsFalse(item.HasStoredSecret, "the custom account holds nothing yet");

        item.Mode = ProviderMode.Live;
        Assert.IsTrue(item.HasStoredSecret);
    }

    [TestMethod]
    public void DetachStopsListeningToTheHost()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: true));
        model.Detach();
        host.PersistError = "Could not save.";
        host.RaiseChanged();
        Assert.IsNull(model.Notice, "a closed window must not keep handling host events");
    }

    [TestMethod]
    public void ADismissedPersistFailureStaysDismissedUntilANewOne()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: true));
        host.PersistError = "Could not save.";
        host.RaiseChanged();
        Assert.IsNotNull(model.Notice);

        model.DismissNotice();
        host.RaiseChanged();
        Assert.IsNull(model.Notice, "the same failure must not reappear on the next host event");

        host.PersistError = "Access to the settings file was refused.";
        host.RaiseChanged();
        Assert.AreEqual("Access to the settings file was refused.", model.Notice?.Text);
    }

    [TestMethod]
    public void FlushLeavesTheRefetchDecisionToTheHost()
    {
        // Closing within the save debounce used to force refetch:false, so a provider enabled a
        // moment earlier never got its first reading.
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: false));
        model.Providers[0].Enabled = true;
        host.Applies.Clear();
        model.Flush();
        Assert.HasCount(1, host.Applies);
        Assert.IsNull(host.Applies[0].Refetch);
    }

    [TestMethod]
    public void FlushOnAnInvalidConnectorAsksForNothingNew()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Custom, Enabled: true, Mode: ProviderMode.CustomJson));
        var before = FetchInputs.From(model.BuildPreferences());
        model.Providers[0].Endpoint = "http://example.com/usage";
        host.Applies.Clear();
        model.Flush();
        Assert.AreEqual(before, FetchInputs.From(host.Applies[0].Preferences));
    }

    [TestMethod]
    public void TheLastReadCaptionIsRedrawnOnATick()
    {
        var (model, host) = Session(new ProviderConfiguration(ProviderId.Claude, Enabled: true));
        host.LastRefresh = DateTimeOffset.Now.AddMinutes(-4);
        var announced = new List<string?>();
        model.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        model.TickCaption();

        Assert.Contains(nameof(model.RefreshCaption), announced);
        Assert.AreEqual("Last read 4 min ago", model.RefreshCaption);
    }

    [TestMethod]
    public void TheOffsetSliderAndTheStoredFileShareOneRange()
    {
        // Settings clamped to 300 while the store kept 500, so the overlay went on using a nudge
        // the window could not show until the next unrelated change rewrote it.
        var (model, _) = Session();
        Assert.AreEqual(OverlayOffset.Max, PreferencesMigration.Migrate(AppPreferences.Defaults with { VerticalOffset = 500 }).VerticalOffset);

        model.VerticalOffset = -900;
        Assert.AreEqual(-300d, model.VerticalOffset);
    }

    [TestMethod]
    public void ProvidersWithNoWindowsReaderOfferNoBuiltInSecretField()
    {
        // Warp owns a secret account for macOS, but the Windows reader always throws. Offering a
        // key field here would take a credential that can never produce a reading.
        var (model, _) = Session(
            new ProviderConfiguration(ProviderId.Warp, Enabled: true, Mode: ProviderMode.Live),
            new ProviderConfiguration(ProviderId.Cursor, Enabled: true, Mode: ProviderMode.Live),
            new ProviderConfiguration(ProviderId.OpenRouter, Enabled: true, Mode: ProviderMode.Live));
        var warp = model.Providers[0];
        Assert.IsFalse(warp.ShowLiveSecret);
        Assert.IsFalse(warp.ShowSecretRow);
        Assert.IsTrue(warp.ShowUnavailableCopy);
        Assert.IsTrue(model.Providers[1].ShowUnavailableCopy);
        Assert.IsTrue(model.Providers[2].ShowLiveSecret, "a provider the Windows build can read still asks");
        Assert.IsFalse(model.Providers[2].ShowUnavailableCopy);
    }

    private static System.Windows.Controls.ListBox Sidebar(SettingsViewModel model)
    {
        var list = new System.Windows.Controls.ListBox { DataContext = model };
        list.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
            new System.Windows.Data.Binding(nameof(model.FilteredProviders)));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
            new System.Windows.Data.Binding(nameof(model.SelectedProvider)));
        var host = new System.Windows.Controls.ContentControl { Content = list };
        host.Measure(new Size(300, 400));
        host.Arrange(new Rect(0, 0, 300, 400));
        host.UpdateLayout();
        return list;
    }

    private static (SettingsViewModel Model, FakeHost Host) Session(params ProviderConfiguration[] providers)
        => SessionWith(new MemorySecretStore(), providers);

    private static (SettingsViewModel Model, FakeHost Host) SessionWith(MemorySecretStore secrets, params ProviderConfiguration[] providers)
    {
        var list = providers.Length == 0 ? new[] { new ProviderConfiguration(ProviderId.Claude, Enabled: true) } : providers;
        var host = new FakeHost();
        var model = new SettingsViewModel(AppPreferences.Defaults with { Providers = list }, secrets, host);
        return (model, host);
    }

    private sealed class FakeHost : ISettingsHost
    {
        public List<(AppPreferences Preferences, bool? Refetch)> Applies { get; } = [];
        public IReadOnlyList<ProviderSnapshot> Snapshots { get; set; } = [];
        public DateTimeOffset? LastRefresh { get; set; }
        public bool IsRefreshing { get; set; }
        public string? PersistError { get; set; }
        public int RefreshCalls { get; private set; }
        public event EventHandler? HostChanged;
        public void Apply(AppPreferences preferences, bool? refetch = null) => Applies.Add((preferences, refetch));
        public Task RefreshNowAsync() { RefreshCalls++; return Task.CompletedTask; }
        public void RaiseChanged() => HostChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public string? Read(string account) => _values.GetValueOrDefault(account);
        public void Write(string account, string? value)
        {
            if (string.IsNullOrEmpty(value)) _values.Remove(account);
            else _values[account] = value;
        }
    }
}

[TestClass]
public sealed class SettingsTypoTests
{
    [TestMethod]
    public void TheSettingsRampUsesInterAndTabularMeta()
    {
        Assert.IsNotNull(Typo.Family);
        Assert.AreEqual(20d, SettingsTypo.PaneTitle.Size);
        Assert.AreEqual(FontWeights.Bold, SettingsTypo.PaneTitle.Weight);
        Assert.IsTrue(SettingsTypo.Meta.TabularDigits);
        Assert.IsFalse(SettingsTypo.Footer.TabularDigits);
        Assert.Contains("Inter", Typo.Family.Source);
    }
}
