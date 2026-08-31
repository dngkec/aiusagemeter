using System.Collections.ObjectModel;
using System.Diagnostics;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Services;

namespace AIUsageMeter.Windows;

internal sealed record Option<T>(T Value, string Label);
internal sealed record DisplayOption(string? Id, string Name);

internal sealed class SettingsViewModel : BindableBase
{
    private readonly AppPreferences _original;
    private readonly ISecretStore _secrets;
    private ProviderSettingsItem? _selectedProvider;
    private string _validationMessage = "";
    public SettingsViewModel(AppPreferences preferences, ISecretStore secrets)
    {
        _original = preferences; _secrets = secrets;
        Providers = new(preferences.Providers.Select(x => new ProviderSettingsItem(x, SecretExists(x, secrets))));
        SelectedProvider = Providers.FirstOrDefault();
        Displays = new(new[] { new DisplayOption(null, "Display with pointer") }.Concat(ScreenPlacementService.Screens.Select(x => new DisplayOption(x.Id, x.Name))));
        RefreshIntervalSeconds = preferences.RefreshIntervalSeconds; ScreenIdentifier = preferences.ScreenIdentifier;
        VerticalPosition = preferences.VerticalPosition; VerticalOffset = preferences.VerticalOffset; OverlayVisible = preferences.OverlayVisible;
        LaunchAtLogin = StartupService.IsEnabled; DemoData = preferences.DemoData; OverlaySize = preferences.OverlaySize;
    }

    public ObservableCollection<ProviderSettingsItem> Providers { get; }
    public ObservableCollection<DisplayOption> Displays { get; }
    public IReadOnlyList<Option<double>> RefreshIntervals { get; } = [new(30, "30 seconds"), new(60, "1 minute"), new(300, "5 minutes"), new(900, "15 minutes"), new(3600, "1 hour")];
    public IReadOnlyList<VerticalPosition> VerticalPositions { get; } = Enum.GetValues<VerticalPosition>();
    public IReadOnlyList<OverlaySize> OverlaySizes { get; } = Enum.GetValues<OverlaySize>();
    public double RefreshIntervalSeconds { get; set; }
    public string? ScreenIdentifier { get; set; }
    public VerticalPosition VerticalPosition { get; set; }
    public double VerticalOffset { get; set; }
    public bool LaunchAtLogin { get; set; }
    public bool DemoData { get; set; }
    public bool OverlayVisible { get; set; }
    public OverlaySize OverlaySize { get; set; }
    public ProviderSettingsItem? SelectedProvider { get => _selectedProvider; set { if (_selectedProvider is not null) _selectedProvider.IsSelected = false; if (Set(ref _selectedProvider, value) && value is not null) value.IsSelected = true; } }
    public string ValidationMessage { get => _validationMessage; set => Set(ref _validationMessage, value); }

    public AppPreferences BuildPreferences()
    {
        foreach (var item in Providers.Where(x => x.Mode == ProviderMode.CustomJson && x.Enabled))
        {
            EndpointPolicy.Validate(item.Endpoint);
            if (!string.IsNullOrWhiteSpace(item.DashboardUrl)) EndpointPolicy.Validate(item.DashboardUrl);
        }
        return PreferencesMigration.Migrate(_original with
        {
            Providers = Providers.Select(x => x.Build()).ToList(), RefreshIntervalSeconds = RefreshIntervalSeconds,
            ScreenIdentifier = ScreenIdentifier, VerticalPosition = VerticalPosition, VerticalOffset = VerticalOffset,
            LaunchAtLogin = LaunchAtLogin, DemoData = DemoData, OverlayVisible = OverlayVisible, OverlaySize = OverlaySize
        });
    }

    public void CommitStartupSetting() => StartupService.SetEnabled(LaunchAtLogin);

    public Task CommitSecretsAsync()
    {
        foreach (var item in Providers)
        {
            var account = item.SecretAccount;
            if (account is null) continue;
            if (item.RemoveSecret) _secrets.Write(account, null);
            else if (!string.IsNullOrEmpty(item.NewSecret)) _secrets.Write(account, item.NewSecret);
        }
        return Task.CompletedTask;
    }

    public void MoveSelected(int delta)
    {
        if (SelectedProvider is null) return;
        var current = Providers.IndexOf(SelectedProvider); var target = current + delta;
        if (target < 0 || target >= Providers.Count) return;
        Providers.Move(current, target);
    }

    public void Open(Uri uri)
    {
        if (!SupportLinks.IsAllowed(uri)) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    private static bool SecretExists(ProviderConfiguration configuration, ISecretStore secrets)
    {
        var account = configuration.Mode == ProviderMode.CustomJson ? $"custom.{configuration.Id}" : SecretAccounts.For(configuration.Id);
        if (account is null) return false;
        try { return !string.IsNullOrEmpty(secrets.Read(account)); } catch { return false; }
    }
}

internal sealed class ProviderSettingsItem : BindableBase
{
    private bool _isSelected;
    public ProviderSettingsItem(ProviderConfiguration value, bool secretExists)
    {
        Id = value.Id; Enabled = value.Enabled; ShowInOverlay = value.ShowInOverlay; Mode = value.Mode; MonthlyBudget = value.MonthlyBudget;
        WorkspaceId = value.WorkspaceId; Region = value.Region; ManualUsed = value.ManualValue.Used; ManualLimit = value.ManualValue.Limit; ManualResetDate = value.ManualValue.ResetDate?.LocalDateTime;
        var custom = value.CustomValue; Endpoint = custom.Endpoint; HttpMethod = custom.Method; SecretPlacement = custom.SecretPlacement;
        CustomName = custom.Name; ApiKeyHeader = custom.ApiKeyHeader; PercentPath = custom.PercentPath; UsedPath = custom.UsedPath; LimitPath = custom.LimitPath; ResetPath = custom.ResetPath; DashboardUrl = custom.DashboardUrl;
        SecretStatus = secretExists ? "A secret is saved in Windows Credential Manager." : "No app-owned secret is saved.";
    }
    public ProviderId Id { get; }
    public string Name => Id.DisplayName();
    public bool Enabled { get; set; }
    public bool ShowInOverlay { get; set; }
    public ProviderMode Mode { get; set; }
    public double MonthlyBudget { get; set; }
    public string WorkspaceId { get; set; }
    public ProviderRegion Region { get; set; }
    public double ManualUsed { get; set; }
    public double ManualLimit { get; set; }
    public DateTime? ManualResetDate { get; set; }
    public string CustomName { get; set; }
    public string Endpoint { get; set; }
    public HttpVerb HttpMethod { get; set; }
    public SecretPlacement SecretPlacement { get; set; }
    public string ApiKeyHeader { get; set; }
    public string PercentPath { get; set; }
    public string UsedPath { get; set; }
    public string LimitPath { get; set; }
    public string ResetPath { get; set; }
    public string DashboardUrl { get; set; }
    public string NewSecret { get; set; } = "";
    public bool RemoveSecret { get; set; }
    public string SecretStatus { get; }
    public string? SecretAccount => Mode == ProviderMode.CustomJson ? $"custom.{Id}" : SecretAccounts.For(Id);
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public IReadOnlyList<ProviderMode> Modes { get; } = Enum.GetValues<ProviderMode>();
    public IReadOnlyList<ProviderRegion> Regions { get; } = Enum.GetValues<ProviderRegion>();
    public IReadOnlyList<HttpVerb> HttpMethods { get; } = Enum.GetValues<HttpVerb>();
    public IReadOnlyList<SecretPlacement> SecretPlacements { get; } = Enum.GetValues<SecretPlacement>();
    public string Availability => Id switch
    {
        ProviderId.Cursor => "Windows live integration unavailable: Cursor stores its token in SQLite and AIUsageMeter does not ship or invoke a SQLite reader.",
        ProviderId.JetBrainsAI => "Windows live integration unavailable; Manual Budget and Custom JSON remain available.",
        ProviderId.Warp => "Windows live integration unavailable; Manual Budget and Custom JSON remain available.",
        ProviderId.Perplexity or ProviderId.Windsurf or ProviderId.LocalModels or ProviderId.Amp or ProviderId.Kilo or ProviderId.Augment or ProviderId.Devin or ProviderId.Antigravity or ProviderId.Custom => "No safe built-in endpoint is available; use Custom JSON or Manual Budget.",
        _ => "Live integration available. Shared CLI credentials are discovered read-only; app-owned API keys use Windows Credential Manager."
    };

    public ProviderConfiguration Build() => new(Id, Enabled, ShowInOverlay, Mode, Positive(MonthlyBudget), WorkspaceId.Trim(), Region,
        new ManualBudget(double.IsFinite(ManualUsed) ? Math.Max(0, ManualUsed) : 0, Positive(ManualLimit), ManualResetDate is null ? null : new DateTimeOffset(ManualResetDate.Value)),
        new CustomConnector(string.IsNullOrWhiteSpace(CustomName) ? Name : CustomName.Trim(), Endpoint.Trim(), HttpMethod, SecretPlacement,
            string.IsNullOrWhiteSpace(ApiKeyHeader) ? "X-API-Key" : ApiKeyHeader.Trim(), PercentPath.Trim(), UsedPath.Trim(), LimitPath.Trim(), ResetPath.Trim(), DashboardUrl.Trim()));

    private static double Positive(double value) => double.IsFinite(value) ? Math.Max(0.01, value) : 100;
}
