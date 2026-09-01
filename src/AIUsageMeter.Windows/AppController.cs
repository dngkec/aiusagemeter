using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Overlay;
using AIUsageMeter.Windows.Services;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using DrawingIcon = System.Drawing.Icon;

namespace AIUsageMeter.Windows;

internal sealed class AppController : IDisposable, ISettingsHost
{
    private readonly Dispatcher _dispatcher;
    private readonly PreferencesStore _preferencesStore = new();
    private readonly WindowsCredentialStore _secretStore = new();
    private readonly BoundedHttpClient _httpClient = new();
    private readonly OverlayWindow _overlay;
    private readonly WinForms.NotifyIcon _tray;
    private readonly DispatcherTimer _timer;
    private readonly RefreshCoordinator _coordinator;
    private AppPreferences _preferences;
    private IReadOnlyList<ProviderSnapshot> _snapshots = [];
    private CancellationTokenSource? _refreshCancellation;
    private CancellationTokenSource? _saveCancellation;
    private FetchInputs _fetched;
    /// <summary>Whether the debounced save still owes a reading. Survives a flush on window close.</summary>
    private bool _pendingRefetch;
    private SettingsWindow? _settings;
    private bool _disposed;

    public AppController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher; _preferences = _preferencesStore.Load();
        // Seeded so an Apply that lands before the first reading compares against real inputs.
        _fetched = FetchInputs.From(_preferences);
        var service = new ProviderService(new ProviderContext(_httpClient, _secretStore, new WindowsCredentialDiscovery()));
        _coordinator = new RefreshCoordinator(service);
        _overlay = new OverlayWindow(_preferences, new DispatcherScheduler(dispatcher), ReducedMotion());
        _overlay.PresentationChanged += (_, _) => Reposition();
        _overlay.SettingsRequested += (_, _) => ShowSettings();
        _overlay.SupportRequested += (_, _) => Open(SupportLinks.Sponsor);
        _overlay.DashboardRequested += (_, id) => OpenDashboard(id);
        _overlay.Closing += (_, e) => { if (!_disposed) { e.Cancel = true; _overlay.Hide(); } };
        _tray = new WinForms.NotifyIcon { Icon = LoadIcon(), Text = "AIUsageMeter", Visible = true };
        _tray.DoubleClick += (_, _) => ToggleOverlay();
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _timer.Tick += async (_, _) => await RefreshAsync();
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        SystemEvents.PowerModeChanged += PowerModeChanged;
    }

    public void Start()
    {
        ApplyPresentation();
        BuildTrayMenu();
        _ = RefreshAsync();
    }

    public IReadOnlyList<ProviderSnapshot> Snapshots => _snapshots;
    public DateTimeOffset? LastRefresh { get; private set; }
    public bool IsRefreshing { get; private set; }
    public string? PersistError { get; private set; }
    public event EventHandler? HostChanged;

    public void Apply(AppPreferences preferences, bool? refetch = null)
    {
        var wanted = refetch ?? (FetchInputs.From(preferences) != _fetched);
        _preferences = preferences;
        ApplyPresentation();
        BuildTrayMenu();
        SchedulePersist(wanted);
    }

    public Task RefreshNowAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        HostChanged?.Invoke(this, EventArgs.Empty);
        _refreshCancellation?.Cancel(); _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        var current = _refreshCancellation;
        try
        {
            var results = await _coordinator.RefreshAsync(_preferences, current.Token).ConfigureAwait(false);
            if (current.IsCancellationRequested) return;
            await _dispatcher.InvokeAsync(() =>
            {
                _snapshots = results;
                LastRefresh = DateTimeOffset.Now;
                _fetched = FetchInputs.From(_preferences);
                IsRefreshing = false;
                _overlay.Update(Arrange(results, _preferences.Providers), NoneRefreshing);
                BuildTrayMenu();
                HostChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        finally
        {
            if (current.IsCancellationRequested)
                _ = _dispatcher.InvokeAsync(() => { if (IsRefreshing) { IsRefreshing = false; HostChanged?.Invoke(this, EventArgs.Empty); } });
            if (ReferenceEquals(_refreshCancellation, current))
            {
                _timer.Interval = TimeSpan.FromSeconds(_preferences.RefreshIntervalSeconds);
                _timer.Start();
            }
        }
    }

    private static IReadOnlyList<ProviderSnapshot> Arrange(IEnumerable<ProviderSnapshot> snapshots, IReadOnlyList<ProviderConfiguration> providers)
    {
        var byId = snapshots.ToDictionary(x => x.Id);
        return providers.Where(x => x.Enabled && x.ShowInOverlay).Select(x => byId.GetValueOrDefault(x.Id)).Where(x => x is not null).Cast<ProviderSnapshot>().ToList();
    }

    private void ApplyPresentation()
    {
        _timer.Interval = TimeSpan.FromSeconds(_preferences.RefreshIntervalSeconds);
        _overlay.Apply(_preferences);
        if (_preferences.OverlayVisible)
        {
            if (!_overlay.IsVisible) _overlay.Show();
            Reposition();
        }
        else _overlay.Hide();
    }

    private void Reposition()
    {
        if (!_overlay.IsVisible) return;
        ScreenPlacementService.Place(_overlay, _preferences);
    }

    private void OpenDashboard(ProviderId id)
    {
        if (_snapshots.FirstOrDefault(x => x.Id == id)?.DashboardUrl is { } url) Open(url);
    }

    /// <summary>Honours the system's "show animations" setting, as macOS honours reduce motion.</summary>
    private static bool ReducedMotion() => !System.Windows.SystemParameters.ClientAreaAnimation;

    private static readonly IReadOnlySet<ProviderId> NoneRefreshing = new HashSet<ProviderId>();

    private void ToggleOverlay()
    {
        _preferences = _preferences with { OverlayVisible = !_preferences.OverlayVisible };
        _ = SavePreferencesAsync(); ApplyPresentation(); BuildTrayMenu();
    }

    private void ShowSettings()
    {
        if (_settings is not null) { if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal; _settings.Activate(); return; }
        var window = new SettingsWindow(new SettingsViewModel(_preferences, _secretStore, this));
        window.Closed += (_, _) =>
        {
            // Only clear the field if this is still the open window: reopening is faster than a save.
            if (ReferenceEquals(_settings, window)) _settings = null;
            _ = FlushPendingAsync();
        };
        _settings = window;
        window.Show(); window.Activate();
    }

    /// <summary>
    /// Writes the debounced save now rather than 350 ms after the window has gone, keeping the
    /// reading that change was owed. Cancelling the debounce alone used to drop the refetch.
    /// </summary>
    private async Task FlushPendingAsync()
    {
        var refetch = _pendingRefetch;
        _pendingRefetch = false;
        _saveCancellation?.Cancel();
        await SavePreferencesAsync(fromSettings: true).ConfigureAwait(false);
        ApplyStartupPreference();
        if (refetch) await RefreshAsync().ConfigureAwait(false);
        else _fetched = FetchInputs.From(_preferences);
    }

    private void SchedulePersist(bool refetch)
    {
        _pendingRefetch |= refetch;
        _saveCancellation?.Cancel();
        _saveCancellation = new CancellationTokenSource();
        var token = _saveCancellation.Token;
        _ = PersistAsync(refetch, token);
    }

    private async Task PersistAsync(bool refetch, CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token).ConfigureAwait(false);
            _pendingRefetch = false;
            await SavePreferencesAsync(fromSettings: true).ConfigureAwait(false);
            ApplyStartupPreference();
            if (refetch) await RefreshAsync().ConfigureAwait(false);
            else _fetched = FetchInputs.From(_preferences);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Applies "launch at sign-in", reporting a registry refusal as a settings notice.</summary>
    private void ApplyStartupPreference()
    {
        try { StartupService.SetEnabled(_preferences.LaunchAtLogin); }
        catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            PersistError = error.Message;
            _ = _dispatcher.InvokeAsync(() => HostChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    private async Task SavePreferencesAsync(bool fromSettings = false)
    {
        try
        {
            await _preferencesStore.SaveAsync(_preferences);
            if (fromSettings) PersistError = null;
        }
        catch (IOException) { await HandlePersistFailure(fromSettings).ConfigureAwait(false); }
        catch (UnauthorizedAccessException) { await HandlePersistFailure(fromSettings).ConfigureAwait(false); }
    }

    private async Task HandlePersistFailure(bool fromSettings)
    {
        if (fromSettings)
        {
            PersistError = "Could not save.";
            await _dispatcher.InvokeAsync(() => HostChanged?.Invoke(this, EventArgs.Empty));
            return;
        }
        System.Windows.MessageBox.Show("Preferences could not be saved.", "AIUsageMeter", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void BuildTrayMenu()
    {
        var menu = new WinForms.ContextMenuStrip();
        var visible = Arrange(_snapshots, _preferences.Providers);
        if (visible.Count > 0)
        {
            var ready = visible.Where(x => x.Status == ProviderStatus.Ready).ToList();
            if (ready.Count > 1)
            {
                var average = ready.Average(x => x.PrimaryPercent);
                menu.Items.Add(new WinForms.ToolStripMenuItem($"Average {average:F0}% across {ready.Count} subscriptions") { Enabled = false });
                menu.Items.Add(new WinForms.ToolStripSeparator());
            }
            foreach (var provider in visible)
            {
                var caption = provider.Status == ProviderStatus.Ready ? $"{provider.PrimaryPercent:F0}%" : provider.Status.ToString();
                var item = new WinForms.ToolStripMenuItem($"{provider.Name}    {caption}") { Enabled = provider.DashboardUrl is not null, Tag = provider.DashboardUrl };
                item.Click += (_, _) => { if (item.Tag is Uri uri) Open(uri); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new WinForms.ToolStripSeparator());
        }
        menu.Items.Add("Refresh Now", null, async (_, _) => await RefreshAsync());
        menu.Items.Add(_preferences.OverlayVisible ? "Hide Overlay" : "Show Overlay", null, (_, _) => ToggleOverlay());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Buy Me a Coffee…", null, (_, _) => Open(SupportLinks.Sponsor));
        menu.Items.Add("AIUsageMeter on GitHub…", null, (_, _) => Open(SupportLinks.Repository));
        menu.Items.Add("Report an Issue…", null, (_, _) => Open(SupportLinks.Issues));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quit AIUsageMeter", null, (_, _) => Quit());
        var old = _tray.ContextMenuStrip; _tray.ContextMenuStrip = menu; old?.Dispose();
        var averageText = visible.Where(x => x.Status == ProviderStatus.Ready).Select(x => x.PrimaryPercent).DefaultIfEmpty().Average();
        _tray.Text = visible.Count == 0 ? "AIUsageMeter" : $"AIUsageMeter · {averageText:F0}% average";
    }

    /// <summary>
    /// The packed icon at the size the shell asks trays for. Reading it off the exe instead would
    /// hand back whichever single image the shell chose, and nothing at all before the exe had an
    /// icon of its own.
    /// </summary>
    private static DrawingIcon LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/AIUsageMeter;component/Assets/AppIcon.ico"));
            if (resource?.Stream is { } stream)
                using (stream) return new DrawingIcon(stream, WinForms.SystemInformation.SmallIconSize);
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException) { }
        return System.Drawing.SystemIcons.Application;
    }

    private static void Open(Uri uri)
    {
        if (!SupportLinks.IsAllowed(uri))
        {
            try { EndpointPolicy.Validate(uri.AbsoluteUri); }
            catch (UsageMeterException) { return; }
        }
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e) => _dispatcher.Invoke(Reposition);
    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) _dispatcher.InvokeAsync(RefreshAsync); }
    private void Quit() { Dispose(); System.Windows.Application.Current.Shutdown(); }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged; SystemEvents.PowerModeChanged -= PowerModeChanged;
        _timer.Stop(); _refreshCancellation?.Cancel(); _refreshCancellation?.Dispose();
        _saveCancellation?.Cancel(); _saveCancellation?.Dispose();
        _tray.Visible = false; _tray.Dispose(); _overlay.Close(); _httpClient.Dispose();
    }
}
