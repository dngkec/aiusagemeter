using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Services;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using DrawingIcon = System.Drawing.Icon;

namespace AIUsageMeter.Windows;

internal sealed class AppController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly PreferencesStore _preferencesStore = new();
    private readonly WindowsCredentialStore _secretStore = new();
    private readonly BoundedHttpClient _httpClient = new();
    private readonly OverlayViewModel _overlayModel = new();
    private readonly OverlayWindow _overlay;
    private readonly WinForms.NotifyIcon _tray;
    private readonly DispatcherTimer _timer;
    private readonly RefreshCoordinator _coordinator;
    private AppPreferences _preferences;
    private IReadOnlyList<ProviderSnapshot> _snapshots = [];
    private CancellationTokenSource? _refreshCancellation;
    private SettingsWindow? _settings;
    private bool _disposed;

    public AppController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher; _preferences = _preferencesStore.Load();
        var service = new ProviderService(new ProviderContext(_httpClient, _secretStore, new WindowsCredentialDiscovery()));
        _coordinator = new RefreshCoordinator(service);
        _overlay = new OverlayWindow(_overlayModel);
        _overlay.PresentationChanged += (_, _) => Reposition();
        _overlay.SizeChanged += (_, _) => Reposition();
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

    private async Task RefreshAsync()
    {
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
                var shown = Arrange(results, _preferences.Providers);
                _overlayModel.Replace(shown);
                Reposition(); BuildTrayMenu();
            });
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        finally
        {
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
        _overlay.Dispatcher.BeginInvoke(() =>
        {
            _overlay.Measure(new System.Windows.Size(_overlay.Width, double.PositiveInfinity));
            ScreenPlacementService.Place(_overlay, _preferences);
        }, DispatcherPriority.Loaded);
    }

    private void ToggleOverlay()
    {
        _preferences = _preferences with { OverlayVisible = !_preferences.OverlayVisible };
        _ = SavePreferencesAsync(); ApplyPresentation(); BuildTrayMenu();
    }

    private void ShowSettings()
    {
        if (_settings is { IsVisible: true }) { _settings.Activate(); return; }
        _settings = new SettingsWindow(new SettingsViewModel(_preferences, _secretStore));
        _settings.Saved += async (_, value) =>
        {
            _preferences = value; await SavePreferencesAsync(); ApplyPresentation(); await RefreshAsync();
        };
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show(); _settings.Activate();
    }

    private async Task SavePreferencesAsync()
    {
        try { await _preferencesStore.SaveAsync(_preferences); }
        catch (IOException) { System.Windows.MessageBox.Show("Preferences could not be saved.", "AIUsageMeter", MessageBoxButton.OK, MessageBoxImage.Warning); }
        catch (UnauthorizedAccessException) { System.Windows.MessageBox.Show("Preferences could not be saved.", "AIUsageMeter", MessageBoxButton.OK, MessageBoxImage.Warning); }
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

    private static DrawingIcon LoadIcon()
    {
        try { if (Environment.ProcessPath is { } path && DrawingIcon.ExtractAssociatedIcon(path) is { } icon) return icon; }
        catch (ArgumentException) { }
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
        _tray.Visible = false; _tray.Dispose(); _overlay.Close(); _httpClient.Dispose();
    }
}
