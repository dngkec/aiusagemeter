using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIUsageMeter.Core;
using AIUsageMeter.Windows;

namespace AIUsageMeter.Windows.Tests;

/// <summary>Writes the Settings panes to PNGs so the layout can be looked at.</summary>
[TestClass]
public sealed class SettingsSheet
{
    [TestMethod]
    public void WriteSettingsSheet()
    {
        var folder = Environment.GetEnvironmentVariable("AIUSAGEMETER_SETTINGS_SHEET");
        if (string.IsNullOrEmpty(folder))
        {
            Assert.Inconclusive("Set AIUSAGEMETER_SETTINGS_SHEET to a folder to regenerate the sheet.");
            return;
        }

        var written = Rendering.Sta(() =>
        {
            var paths = new List<string>();
            var model = new SettingsViewModel(AppPreferences.Defaults, new NoSecrets(), new Host());
            var window = new SettingsWindow(model);
            window.Width = 1180;
            window.Height = 1180;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -32000;
            window.Top = -32000;
            window.ShowActivated = false;
            window.Show();
            try
            {
                var provider = model.Providers.First(x => x.Id == ProviderId.XaiAPI);
                provider.Enabled = true;
                provider.Mode = ProviderMode.Live;
                model.SelectedProvider = provider;
                paths.Add(Shot(window, folder, "provider-live"));

                provider.Mode = ProviderMode.Manual;
                paths.Add(Shot(window, folder, "provider-manual"));

                provider.Mode = ProviderMode.CustomJson;
                provider.SecretPlacement = SecretPlacement.ApiKeyHeader;
                paths.Add(Shot(window, folder, "provider-custom"));

                model.SelectGeneral();
                paths.Add(Shot(window, folder, "general"));

                model.SelectAbout();
                paths.Add(Shot(window, folder, "about"));
            }
            finally { window.Close(); }
            return paths;
        });

        Assert.IsNotEmpty(written);
    }

    private static string Shot(Window window, string folder, string name)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(root.ActualWidth);
        var height = (int)Math.Ceiling(root.ActualHeight);
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var ground = new DrawingVisual();
        using (var context = ground.RenderOpen())
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
        target.Render(ground);
        target.Render(root);

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private sealed class Host : ISettingsHost
    {
        public IReadOnlyList<ProviderSnapshot> Snapshots => [];
        public DateTimeOffset? LastRefresh => DateTimeOffset.Now.AddMinutes(-3);
        public bool IsRefreshing => false;
        public string? PersistError => null;
        // Nothing here ever raises it: the sheet renders one state per shot.
        public event EventHandler? HostChanged { add { } remove { } }
        public void Apply(AppPreferences preferences, bool? refetch = null) { }
        public Task RefreshNowAsync() => Task.CompletedTask;
    }

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string account) => null;
        public void Write(string account, string? value) { }
    }
}
