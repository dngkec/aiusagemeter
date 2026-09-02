using System.Collections.ObjectModel;
using System.Diagnostics;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Overlay;
using AIUsageMeter.Windows.Services;
using AIUsageMeter.Windows.Settings;

namespace AIUsageMeter.Windows;

internal interface IChoice
{
    object? Value { get; }
    string Label { get; }
}

internal sealed record Option<T>(T Value, string Label) : IChoice
{
    object? IChoice.Value => Value;
}

internal sealed record DisplayOption(string? Id, string Name) : IChoice
{
    object? IChoice.Value => Id;
    string IChoice.Label => Name;
}

internal sealed class SettingsViewModel : BindableBase
{
    private readonly ISecretStore _secrets;
    private readonly ISettingsHost _host;
    private readonly EventHandler _hostChanged;
    private readonly bool _ready;
    /// <summary>True while <see cref="RebuildFilter"/> is editing <see cref="FilteredProviders"/>.</summary>
    private bool _filtering;
    /// <summary>The persist failure already shown, so a dismissed notice does not come back.</summary>
    private string? _seenPersistError;
    private ProviderSettingsItem? _selectedProvider;
    private SettingsSelection _selection = new SettingsSelection.General();
    private string _query = "";
    private SettingsNotice? _notice;
    private double _refreshIntervalSeconds;
    private string? _screenIdentifier;
    private VerticalPosition _verticalPosition;
    private double _verticalOffset;
    private bool _launchAtLogin;
    private bool _demoData;
    private bool _overlayVisible;
    private OverlaySize _overlaySize;

    public SettingsViewModel(AppPreferences preferences, ISecretStore secrets, ISettingsHost host)
    {
        _secrets = secrets;
        _host = host;
        Providers = new(preferences.Providers.Select(x => new ProviderSettingsItem(x, SecretExists, OnProviderChanged)));
        foreach (var item in Providers) item.Attach(this);
        FilteredProviders = new(Providers);
        SelectedProvider = Providers.FirstOrDefault(x => x.Enabled) ?? Providers.FirstOrDefault();
        _selection = SelectedProvider is { } first
            ? new SettingsSelection.Provider(first.Id)
            : new SettingsSelection.General();
        Displays = new(new[] { new DisplayOption(null, "Display with the pointer") }
            .Concat(ScreenPlacementService.Screens.Select(x => new DisplayOption(x.Id, x.Name))));
        if (preferences.ScreenIdentifier is { } stored && Displays.All(x => x.Id != stored))
            Displays.Add(new DisplayOption(stored, "Display not connected"));
        _refreshIntervalSeconds = preferences.RefreshIntervalSeconds;
        _screenIdentifier = preferences.ScreenIdentifier;
        _verticalPosition = preferences.VerticalPosition;
        _verticalOffset = OverlayOffset.Clamp(preferences.VerticalOffset);
        _overlayVisible = preferences.OverlayVisible;
        _launchAtLogin = StartupService.IsEnabled;
        _demoData = preferences.DemoData;
        _overlaySize = preferences.OverlaySize;
        if (SelectedProvider is not null) SelectedProvider.IsSelected = true;
        BindSnapshots();
        _hostChanged = (_, _) => BindSnapshots();
        _host.HostChanged += _hostChanged;
        _ready = true;
    }

    public ObservableCollection<ProviderSettingsItem> Providers { get; }
    public ObservableCollection<ProviderSettingsItem> FilteredProviders { get; }
    public ObservableCollection<DisplayOption> Displays { get; }
    public IReadOnlyList<Option<double>> RefreshIntervals { get; } =
        [new(30, "30 seconds"), new(60, "1 minute"), new(300, "5 minutes"), new(900, "15 minutes"), new(3600, "1 hour")];
    public IReadOnlyList<Option<VerticalPosition>> VerticalPositions { get; } =
        [new(VerticalPosition.Top, "Top"), new(VerticalPosition.Center, "Centre"), new(VerticalPosition.Bottom, "Bottom")];
    public IReadOnlyList<Option<OverlaySize>> OverlaySizes { get; } =
        [new(OverlaySize.Small, "Small"), new(OverlaySize.Medium, "Medium"), new(OverlaySize.Large, "Large")];

    public double RefreshIntervalSeconds { get => _refreshIntervalSeconds; set { if (Set(ref _refreshIntervalSeconds, value)) Commit(false); } }
    public string? ScreenIdentifier { get => _screenIdentifier; set { if (Set(ref _screenIdentifier, value)) Commit(false); } }
    public VerticalPosition VerticalPosition { get => _verticalPosition; set { if (Set(ref _verticalPosition, value)) Commit(false); } }
    public double VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            if (!Set(ref _verticalOffset, OverlayOffset.Clamp(value))) return;
            Raise(nameof(OffsetIsNonZero));
            Commit(false);
        }
    }
    public bool LaunchAtLogin { get => _launchAtLogin; set { if (Set(ref _launchAtLogin, value)) Commit(false); } }
    public bool DemoData { get => _demoData; set { if (Set(ref _demoData, value)) Commit(); } }
    public bool OverlayVisible { get => _overlayVisible; set { if (Set(ref _overlayVisible, value)) Commit(false); } }
    public OverlaySize OverlaySize { get => _overlaySize; set { if (Set(ref _overlaySize, value)) Commit(false); } }
    public bool OffsetIsNonZero => VerticalOffset != 0;

    public ProviderSettingsItem? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            // Rebuilding the filtered list drops the sidebar's selection, and the two-way binding
            // writes that null straight back. The pane must not blank out for a search keystroke.
            if (_filtering && value is null) return;
            if (ReferenceEquals(_selectedProvider, value)) return;
            if (_selectedProvider is not null)
            {
                // A half-typed key never follows the user onto another provider.
                _selectedProvider.NewSecret = "";
                _selectedProvider.IsSelected = false;
            }
            Set(ref _selectedProvider, value);
            if (value is not null)
            {
                value.IsSelected = true;
                value.NewSecret = "";
                Selection = new SettingsSelection.Provider(value.Id);
            }
            Raise(nameof(IsProviderPane));
        }
    }

    public SettingsSelection Selection
    {
        get => _selection;
        set
        {
            if (!Set(ref _selection, value)) return;
            if (_selectedProvider is not null && value is not SettingsSelection.Provider)
            {
                _selectedProvider.NewSecret = "";
                _selectedProvider.IsSelected = false;
                _selectedProvider = null;
                Raise(nameof(SelectedProvider));
            }
            Notice = null;
            Raise(nameof(IsGeneralPane));
            Raise(nameof(IsAboutPane));
            Raise(nameof(IsProviderPane));
        }
    }

    public bool IsGeneralPane => Selection is SettingsSelection.General;
    public bool IsAboutPane => Selection is SettingsSelection.About;
    public bool IsProviderPane => Selection is SettingsSelection.Provider;

    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value)) return;
            RebuildFilter();
            Raise(nameof(CanReorder));
            Raise(nameof(ShowSearchPlaceholder));
            Raise(nameof(SidebarHint));
            Raise(nameof(FilterEmpty));
            Raise(nameof(FilterEmptyMessage));
            foreach (var item in Providers) item.RaisePosition();
        }
    }

    public bool CanReorder => string.IsNullOrWhiteSpace(Query);
    public bool ShowSearchPlaceholder => string.IsNullOrEmpty(Query);
    public bool FilterEmpty => FilteredProviders.Count == 0;
    public string FilterEmptyMessage => $"No provider matches “{Query}”.";
    public string SidebarHint => CanReorder ? "Drag a provider to reorder the rail." : "Clear the search to reorder.";
    public SettingsNotice? Notice { get => _notice; set => Set(ref _notice, value); }
    public bool IsRefreshing => _host.IsRefreshing;
    public string RefreshCaption
    {
        get
        {
            if (_host.IsRefreshing) return "Reading usage…";
            if (_host.LastRefresh is { } last) return $"Last read {RelativeTime.Short(last, DateTimeOffset.Now)}";
            return "No reading yet";
        }
    }

    public string VersionSummary
    {
        get
        {
            var version = typeof(SettingsViewModel).Assembly.GetName().Version;
            return version is null ? "AIUsageMeter for Windows" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string UpdateSummary => _host.Update.Summary;
    public bool HasUpdateSummary => _host.Update.Summary.Length > 0;
    public bool UpdateIsFailure => _host.Update.Stage == UpdateStage.Failed;
    public bool CanCheckForUpdates => !_host.Update.IsBusy;
    /// <summary>Shown only once a version has been found, and never while it is installing.</summary>
    public bool CanInstallUpdate => _host.Update.CanInstall;
    public string InstallUpdateLabel => _host.Update.Package is { } package ? $"Update to {package.Version}" : "Update";
    public bool HasReleaseNotes => _host.Update.Package?.Page is not null;

    /// <summary>Both report through <see cref="UpdateSummary"/>; neither ever faults.</summary>
    public Task CheckForUpdatesAsync() => _host.CheckForUpdatesAsync();
    public Task InstallUpdateAsync() => _host.InstallUpdateAsync();

    public void OpenReleaseNotes()
    {
        if (_host.Update.Package?.Page is not { } page) return;
        // Not a SupportLinks entry, so it is checked the way a provider endpoint is.
        try { EndpointPolicy.Validate(page.AbsoluteUri); }
        catch (UsageMeterException) { return; }
        Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
    }

    public void SelectGeneral() => Selection = new SettingsSelection.General();
    public void SelectAbout() => Selection = new SettingsSelection.About();

    public void SelectPrevious()
    {
        if (Selection is SettingsSelection.Provider)
        {
            var index = SelectedProvider is null ? 0 : FilteredProviders.IndexOf(SelectedProvider);
            if (index <= 0) SelectAbout();
            else SelectedProvider = FilteredProviders[index - 1];
        }
        else if (Selection is SettingsSelection.About) SelectGeneral();
    }

    public void SelectNext()
    {
        if (Selection is SettingsSelection.General) SelectAbout();
        else if (Selection is SettingsSelection.About)
        {
            if (FilteredProviders.Count > 0) SelectedProvider = FilteredProviders[0];
        }
        else if (SelectedProvider is { } current)
        {
            var index = FilteredProviders.IndexOf(current);
            if (index >= 0 && index < FilteredProviders.Count - 1) SelectedProvider = FilteredProviders[index + 1];
        }
    }

    public void ResetOffset()
    {
        VerticalOffset = 0;
    }

    public void DismissNotice() => Notice = null;

    /// <summary>
    /// Re-reads "last read 3 min ago". The caption counts up from a fixed moment, so nothing
    /// changes it on its own; the window ticks this while it is open.
    /// </summary>
    public void TickCaption() => Raise(nameof(RefreshCaption));

    public Task RefreshNowAsync() => _host.RefreshNowAsync();

    public AppPreferences BuildPreferences() => PreferencesMigration.Migrate(new AppPreferences(
        AppPreferences.CurrentSchemaVersion,
        Providers.Select(x => x.Build()).ToList(),
        RefreshIntervalSeconds, ScreenIdentifier, VerticalPosition, VerticalOffset,
        LaunchAtLogin, DemoData, OverlayVisible, OverlaySize));

    public void MoveSelected(int delta)
    {
        if (SelectedProvider is null || !CanReorder) return;
        Move(SelectedProvider, delta);
    }

    public void Move(ProviderSettingsItem item, int delta)
    {
        if (!CanReorder) return;
        var current = Providers.IndexOf(item);
        var target = current + delta;
        if (current < 0 || target < 0 || target >= Providers.Count) return;
        Providers.Move(current, target);
        RebuildFilter();
        foreach (var provider in Providers) provider.RaisePosition();
        Commit(false);
    }

    public void SaveSecret()
    {
        var item = SelectedProvider;
        if (item is null || string.IsNullOrWhiteSpace(item.NewSecret)) return;
        var account = item.SecretAccount;
        if (account is null) return;
        try
        {
            _secrets.Write(account, item.NewSecret.Trim());
            item.NewSecret = "";
            item.MarkSecret(true);
            Notice = SettingsNotice.Success("Saved in Credential Manager");
            _ = _host.RefreshNowAsync();
        }
        catch (Exception error) when (IsSecretFailure(error))
        {
            Notice = SettingsNotice.Failure(error.Message);
        }
    }

    public void RemoveSecret()
    {
        var item = SelectedProvider;
        var account = item?.SecretAccount;
        if (item is null || account is null || !item.HasStoredSecret) return;
        try
        {
            _secrets.Write(account, null);
            item.NewSecret = "";
            item.MarkSecret(false);
            Notice = SettingsNotice.Success("Removed from Credential Manager");
            _ = _host.RefreshNowAsync();
        }
        catch (Exception error) when (IsSecretFailure(error))
        {
            Notice = SettingsNotice.Failure(error.Message);
        }
    }

    public void Open(Uri uri)
    {
        if (!SupportLinks.IsAllowed(uri)) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>
    /// The last word before the window closes. Refetch is left to the host: closing within the
    /// save debounce must not lose the reading a just-enabled provider is owed.
    /// </summary>
    public void Flush() => _host.Apply(BuildPreferences());

    /// <summary>Stops listening to the host. Called when the window closes.</summary>
    public void Detach() => _host.HostChanged -= _hostChanged;

    internal void Commit(bool? refetch = null)
    {
        if (!_ready) return;
        // An unusable connector no longer needs suppressing here: FetchInputs leaves that provider
        // out, so the host sees no change for it while still refetching every other one.
        _host.Apply(BuildPreferences(), refetch);
    }

    private void OnProviderChanged() => Commit();

    /// <summary>
    /// Syncs <see cref="FilteredProviders"/> item by item. Clearing and refilling would reset the
    /// sidebar's selection, and the two-way binding would push that null into
    /// <see cref="SelectedProvider"/> — blanking the detail pane on every search keystroke.
    /// </summary>
    private void RebuildFilter()
    {
        _filtering = true;
        try
        {
            var wanted = Providers.Where(Matches).ToList();
            for (var index = FilteredProviders.Count - 1; index >= 0; index--)
                if (!wanted.Contains(FilteredProviders[index])) FilteredProviders.RemoveAt(index);
            for (var index = 0; index < wanted.Count; index++)
            {
                var at = FilteredProviders.IndexOf(wanted[index]);
                if (at < 0) FilteredProviders.Insert(index, wanted[index]);
                else if (at != index) FilteredProviders.Move(at, index);
            }
        }
        finally { _filtering = false; }
    }

    private bool Matches(ProviderSettingsItem item)
        => string.IsNullOrWhiteSpace(Query) || item.Name.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase);

    private void BindSnapshots()
    {
        foreach (var item in Providers)
            item.Snapshot = _host.Snapshots.FirstOrDefault(x => x.Id == item.Id);
        var error = _host.PersistError;
        if (error is null) _seenPersistError = null;
        else if (error != _seenPersistError)
        {
            _seenPersistError = error;
            Notice = SettingsNotice.Failure(error);
        }
        Raise(nameof(IsRefreshing));
        Raise(nameof(RefreshCaption));
        Raise(nameof(UpdateSummary));
        Raise(nameof(HasUpdateSummary));
        Raise(nameof(UpdateIsFailure));
        Raise(nameof(CanCheckForUpdates));
        Raise(nameof(CanInstallUpdate));
        Raise(nameof(InstallUpdateLabel));
        Raise(nameof(HasReleaseNotes));
    }

    internal static string? EndpointWarning(string endpoint)
    {
        var value = endpoint.Trim();
        if (value.Length == 0) return null;
        try { EndpointPolicy.Validate(value); return null; }
        catch (UsageMeterException error) { return error.Message; }
    }

    private bool SecretExists(string? account)
    {
        if (account is null) return false;
        try { return !string.IsNullOrEmpty(_secrets.Read(account)); } catch { return false; }
    }

    private static bool IsSecretFailure(Exception error) => error is UnauthorizedAccessException or System.IO.IOException
        or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException;
}

internal sealed class ProviderSettingsItem : BindableBase
{
    /// <summary>
    /// Providers whose Windows reader always throws. They own a secret account for macOS, so
    /// asking for a key here would offer a field that can never produce a reading.
    /// </summary>
    private static readonly IReadOnlySet<ProviderId> NoWindowsReader =
        new HashSet<ProviderId> { ProviderId.Cursor, ProviderId.JetBrainsAI, ProviderId.Warp };

    private readonly Action _changed;
    private readonly Func<string?, bool> _secretExists;
    private SettingsViewModel? _owner;
    private bool _isSelected;
    private bool _enabled;
    private bool _showInOverlay;
    private ProviderMode _mode;
    private double _monthlyBudget;
    private string _workspaceId;
    private ProviderRegion _region;
    private double _manualUsed;
    private double _manualLimit;
    private DateTime? _manualResetDate;
    private string _customName;
    private string _endpoint;
    private HttpVerb _httpMethod;
    private SecretPlacement _secretPlacement;
    private string _apiKeyHeader;
    private string _percentPath;
    private string _usedPath;
    private string _limitPath;
    private string _resetPath;
    private string _dashboardUrl;
    private string _newSecret = "";
    private bool _hasStoredSecret;
    private ProviderSnapshot? _snapshot;

    public ProviderSettingsItem(ProviderConfiguration value, Func<string?, bool> secretExists, Action changed)
    {
        _changed = changed;
        _secretExists = secretExists;
        Id = value.Id;
        _enabled = value.Enabled;
        _showInOverlay = value.ShowInOverlay;
        _mode = value.Mode;
        _monthlyBudget = value.MonthlyBudget;
        _workspaceId = value.WorkspaceId;
        _region = value.Region;
        _manualUsed = value.ManualValue.Used;
        _manualLimit = value.ManualValue.Limit;
        _manualResetDate = value.ManualValue.ResetDate?.LocalDateTime;
        var custom = value.CustomValue;
        _endpoint = custom.Endpoint;
        _httpMethod = custom.Method;
        _secretPlacement = custom.SecretPlacement;
        _customName = custom.Name;
        _apiKeyHeader = custom.ApiKeyHeader;
        _percentPath = custom.PercentPath;
        _usedPath = custom.UsedPath;
        _limitPath = custom.LimitPath;
        _resetPath = custom.ResetPath;
        _dashboardUrl = custom.DashboardUrl;
        _hasStoredSecret = secretExists(SecretAccount);
    }

    public ProviderId Id { get; }
    public string Name => Id.DisplayName();
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) { Touch(); Raise(nameof(ShowInOverlayEnabled)); Raise(nameof(Badge)); Raise(nameof(AutomationName)); Raise(nameof(ShowStatus)); Raise(nameof(GaugeStatus)); } } }
    public bool ShowInOverlay { get => _showInOverlay; set { if (Set(ref _showInOverlay, value)) Touch(); } }
    public bool ShowInOverlayEnabled => Enabled;
    public ProviderMode Mode { get => _mode; set { if (Set(ref _mode, value)) { Touch(); RaiseMode(); } } }
    public double MonthlyBudget { get => _monthlyBudget; set { if (Set(ref _monthlyBudget, value)) Touch(); } }
    public string WorkspaceId { get => _workspaceId; set { if (Set(ref _workspaceId, value)) Touch(); } }
    public ProviderRegion Region { get => _region; set { if (Set(ref _region, value)) Touch(); } }
    public double ManualUsed { get => _manualUsed; set { if (Set(ref _manualUsed, value)) Touch(); } }
    public double ManualLimit { get => _manualLimit; set { if (Set(ref _manualLimit, value)) { Touch(); Raise(nameof(ManualLimitWarning)); } } }
    public DateTime? ManualResetDate { get => _manualResetDate; set { if (Set(ref _manualResetDate, value)) { Touch(); Raise(nameof(ManualResetText)); } } }
    public string ManualResetText
    {
        get => ManualResetDate?.ToString("yyyy-MM-dd HH:mm") ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value)) { ManualResetDate = null; return; }
            if (DateTime.TryParse(value, out var parsed)) ManualResetDate = parsed;
        }
    }
    public string CustomName { get => _customName; set { if (Set(ref _customName, value)) Touch(); } }
    public string Endpoint { get => _endpoint; set { if (Set(ref _endpoint, value)) { Touch(); Raise(nameof(ConnectorWarning)); } } }
    public HttpVerb HttpMethod { get => _httpMethod; set { if (Set(ref _httpMethod, value)) Touch(); } }
    public SecretPlacement SecretPlacement { get => _secretPlacement; set { if (Set(ref _secretPlacement, value)) { Touch(); Raise(nameof(ShowApiKeyHeader)); Raise(nameof(ShowCustomSecret)); Raise(nameof(ShowSecretRow)); Raise(nameof(ShowCredentialSection)); } } }
    public string ApiKeyHeader { get => _apiKeyHeader; set { if (Set(ref _apiKeyHeader, value)) Touch(); } }
    public string PercentPath { get => _percentPath; set { if (Set(ref _percentPath, value)) Touch(); } }
    public string UsedPath { get => _usedPath; set { if (Set(ref _usedPath, value)) Touch(); } }
    public string LimitPath { get => _limitPath; set { if (Set(ref _limitPath, value)) Touch(); } }
    public string ResetPath { get => _resetPath; set { if (Set(ref _resetPath, value)) Touch(); } }
    public string DashboardUrl { get => _dashboardUrl; set { if (Set(ref _dashboardUrl, value)) { Touch(); Raise(nameof(ConnectorWarning)); } } }
    public string NewSecret { get => _newSecret; set { if (Set(ref _newSecret, value)) Raise(nameof(CanSaveSecret)); } }
    public bool HasStoredSecret { get => _hasStoredSecret; private set { if (Set(ref _hasStoredSecret, value)) Raise(nameof(SecretStatus)); } }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public ProviderSnapshot? Snapshot { get => _snapshot; set { if (Set(ref _snapshot, value)) { Raise(nameof(Badge)); Raise(nameof(AutomationName)); Raise(nameof(Percent)); Raise(nameof(ShowStatus)); Raise(nameof(StatusLabel)); Raise(nameof(StatusMessage)); Raise(nameof(GaugeStatus)); } } }

    public IReadOnlyList<Option<ProviderMode>> ModeOptions { get; } =
        [new(ProviderMode.Live, "Built-in"), new(ProviderMode.CustomJson, "Custom JSON"), new(ProviderMode.Manual, "Manual")];
    public IReadOnlyList<Option<ProviderRegion>> RegionOptions { get; } =
        [new(ProviderRegion.Global, "Global"), new(ProviderRegion.China, "China")];
    public IReadOnlyList<Option<HttpVerb>> MethodOptions { get; } =
        [new(HttpVerb.Get, "GET"), new(HttpVerb.Post, "POST")];
    public IReadOnlyList<Option<SecretPlacement>> SecretPlacementOptions { get; } =
        [new(SecretPlacement.Bearer, "Bearer token"), new(SecretPlacement.ApiKeyHeader, "API-key header"), new(SecretPlacement.None, "None")];

    public bool ShowLiveSection => Mode == ProviderMode.Live;
    public bool ShowManualSection => Mode == ProviderMode.Manual;
    public bool ShowCustomSection => Mode == ProviderMode.CustomJson;
    public bool HasAppOwnedSecret => SecretAccounts.For(Id) is not null;
    public bool HasLocalSignIn => Id is ProviderId.Claude or ProviderId.Codex or ProviderId.Grok or ProviderId.Copilot or ProviderId.Gemini or ProviderId.Kimi;
    public bool ShowBudget => ShowLiveSection && LiveCredential.UsesMonthlyBudget(Id);
    public bool ShowWorkspace => ShowLiveSection && LiveCredential.WorkspacePrompt(Id) is not null;
    public string WorkspacePrompt => LiveCredential.WorkspacePrompt(Id) ?? "Workspace / team ID";
    public bool ShowRegion => ShowLiveSection && LiveCredential.UsesRegion(Id);
    public bool ShowLiveSecret => ShowLiveSection && HasAppOwnedSecret && !NoWindowsReader.Contains(Id);
    public bool ShowCustomSecret => ShowCustomSection && SecretPlacement != SecretPlacement.None;
    public bool ShowSecretRow => ShowLiveSecret || ShowCustomSecret;

    /// <summary>
    /// The credential card. Custom JSON always shows it, so the placement can be changed back
    /// after it has been set to None and the secret rows have gone.
    /// </summary>
    public bool ShowCredentialSection => ShowCustomSection || ShowSecretRow;
    public bool ShowLocalSignInCopy => ShowLiveSection && !HasAppOwnedSecret && HasLocalSignIn;
    public bool ShowUnavailableCopy => ShowLiveSection && !ShowLiveSecret && !ShowLocalSignInCopy;
    public bool ShowApiKeyHeader => ShowCustomSection && SecretPlacement == SecretPlacement.ApiKeyHeader;
    public string LiveSecretPrompt => ShowLiveSecret ? LiveCredential.Prompt(Id) : "Secret";
    public string? SecretAccount => Mode == ProviderMode.CustomJson ? $"custom.{Id}" : SecretAccounts.For(Id);
    public string SecretStatus => HasStoredSecret ? "A secret is saved in Credential Manager." : "No secret saved yet";
    public bool CanSaveSecret => !string.IsNullOrWhiteSpace(NewSecret);
    public string SupportCopy => SettingsCopy.Text(Id);
    public string ModeExplanation => Mode switch
    {
        ProviderMode.Live => "Built-in reads the service's own usage endpoint with the credential it already has.",
        ProviderMode.CustomJson => "Custom JSON calls an endpoint you define and reads the numbers out of the response.",
        _ => "Manual budget tracks figures you type in. Nothing is fetched."
    };
    public string? ConnectorWarning
    {
        get
        {
            if (Mode != ProviderMode.CustomJson) return null;
            return SettingsViewModel.EndpointWarning(Endpoint)
                ?? (string.IsNullOrWhiteSpace(DashboardUrl) ? null : SettingsViewModel.EndpointWarning(DashboardUrl));
        }
    }
    public string? ManualLimitWarning => Mode == ProviderMode.Manual && ManualLimit <= 0
        ? "Set a limit above zero, or the gauge has nothing to measure against."
        : null;
    public double Percent => Snapshot is { Status: ProviderStatus.Ready } ready ? ready.PrimaryPercent : 0;
    public ProviderStatus GaugeStatus => Enabled ? Snapshot?.Status ?? ProviderStatus.Loading : ProviderStatus.SetupNeeded;
    public string Badge
    {
        get
        {
            if (!Enabled) return "Off";
            if (Snapshot is { Status: ProviderStatus.Ready } ready) return $"{ready.PrimaryPercent:F0}%";
            if (Snapshot is { } snapshot) return snapshot.Status.ShortLabel();
            return "—";
        }
    }
    public string AutomationName => $"{Name}, {Badge}";
    public bool ShowStatus => Enabled && Snapshot is { Status: not ProviderStatus.Ready };
    public string StatusLabel => Snapshot?.Status.ShortLabel() ?? "";
    public string StatusMessage => Snapshot?.Message is { } message && message != StatusLabel ? message : "";
    public int RailIndex => _owner?.Providers.IndexOf(this) ?? 0;
    public string RailPosition => $"{RailIndex + 1} of {_owner?.Providers.Count ?? 1}";
    public bool CanMoveUp => (_owner?.CanReorder ?? false) && RailIndex > 0;
    public bool CanMoveDown => (_owner?.CanReorder ?? false) && RailIndex < (_owner?.Providers.Count ?? 1) - 1;
    public bool IsDimmed => !Enabled;

    public void Attach(SettingsViewModel owner) => _owner = owner;
    public void MarkSecret(bool exists) => HasStoredSecret = exists;
    public void RaisePosition()
    {
        Raise(nameof(RailIndex));
        Raise(nameof(RailPosition));
        Raise(nameof(CanMoveUp));
        Raise(nameof(CanMoveDown));
    }

    public ProviderConfiguration Build() => new(Id, Enabled, ShowInOverlay, Mode, Positive(MonthlyBudget), WorkspaceId.Trim(), Region,
        new ManualBudget(double.IsFinite(ManualUsed) ? Math.Max(0, ManualUsed) : 0, Positive(ManualLimit), ManualResetDate is null ? null : new DateTimeOffset(ManualResetDate.Value)),
        new CustomConnector(string.IsNullOrWhiteSpace(CustomName) ? Name : CustomName.Trim(), Endpoint.Trim(), HttpMethod, SecretPlacement,
            string.IsNullOrWhiteSpace(ApiKeyHeader) ? "X-API-Key" : ApiKeyHeader.Trim(), PercentPath.Trim(), UsedPath.Trim(), LimitPath.Trim(), ResetPath.Trim(), DashboardUrl.Trim()));

    private void Touch() => _changed();
    private void RaiseMode()
    {
        Raise(nameof(ShowLiveSection)); Raise(nameof(ShowManualSection)); Raise(nameof(ShowCustomSection));
        Raise(nameof(ShowBudget)); Raise(nameof(ShowWorkspace)); Raise(nameof(ShowRegion));
        Raise(nameof(ShowLiveSecret)); Raise(nameof(ShowLocalSignInCopy)); Raise(nameof(ShowUnavailableCopy));
        Raise(nameof(ShowApiKeyHeader)); Raise(nameof(ShowCustomSecret)); Raise(nameof(ShowSecretRow)); Raise(nameof(ShowCredentialSection)); Raise(nameof(ModeExplanation));
        Raise(nameof(ConnectorWarning)); Raise(nameof(ManualLimitWarning)); Raise(nameof(SecretAccount)); Raise(nameof(LiveSecretPrompt));
        // Live and Custom JSON store under different accounts, so "a secret is saved" changes too.
        HasStoredSecret = _secretExists(SecretAccount);
    }

    private static double Positive(double value) => double.IsFinite(value) ? Math.Max(0.01, value) : 100;
}

internal static class SettingsCopy
{
    public static string Text(ProviderId id) => id switch
    {
        ProviderId.Claude => "Reads Claude Code’s saved sign-in and the official subscription usage endpoint, read-only.",
        ProviderId.Codex => "Reads ~/.codex/auth.json and the Codex usage endpoint without altering the CLI login.",
        ProviderId.Grok => "Reads ~/.grok/auth.json and Grok CLI billing, including credits when present.",
        ProviderId.Copilot => "Finds an existing Copilot or GitHub CLI token and normalises the official quota data.",
        ProviderId.Gemini => "Uses a valid Gemini CLI access token. Reopen Gemini CLI once it expires.",
        ProviderId.Kimi => "Uses a valid Kimi Code token and never refreshes or modifies it.",
        ProviderId.Cursor => "Windows live integration unavailable: Cursor stores its token in SQLite and AIUsageMeter does not ship or invoke a SQLite reader.",
        ProviderId.JetBrainsAI => "Windows live integration unavailable; Manual Budget and Custom JSON remain available.",
        ProviderId.Warp => "Windows live integration unavailable; Manual Budget and Custom JSON remain available.",
        ProviderId.AnthropicCost => "Reads organisation API cost with an Admin key stored in Credential Manager.",
        ProviderId.OpenAIAPI => "Reads organisation API cost with an OpenAI Admin key stored in Credential Manager.",
        ProviderId.OpenRouter => "Reads OpenRouter credits and the current key’s spend cap.",
        ProviderId.DeepSeek => "Reads the documented DeepSeek balance endpoint against your monthly budget.",
        ProviderId.Mistral => "Reads organisation API cost with a Mistral Admin key stored in Credential Manager.",
        ProviderId.XaiAPI => "Reads the xAI prepaid balance with a Management key. Needs your team ID.",
        ProviderId.Moonshot => "Reads the documented Moonshot balance endpoint. Pick the region the account was created in.",
        ProviderId.Zai => "Reads the z.ai Coding Plan quota with an API key. Pick China mainland for a BigModel account.",
        ProviderId.OpenCode => "Reads the OpenCode Zen usage endpoint with an API key.",
        _ => "No safe built-in usage endpoint is available on Windows. Use Custom JSON or Manual budget."
    };
}
