using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIUsageMeter.Core;

namespace AIUsageMeter.Windows.Overlay;

/// <summary>Runs a delayed action at once, for rendering a still.</summary>
internal sealed class ImmediateScheduler : IDelayScheduler
{
    public IDisposable After(TimeSpan delay, Action action)
    {
        action();
        return new Nothing();
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>
/// Renders the overlay to a PNG and exits, so the Windows build can be put beside the macOS
/// screenshots in <c>docs/</c> and compared.
/// </summary>
/// <remarks>
/// Mirrors the macOS build, which captures a still when <c>AIUSAGEMETER_SNAPSHOT_PATH</c> is set.
/// Demo data throughout, so the same picture comes out every run.
/// </remarks>
internal static class Snapshot
{
    private const string PathVariable = "AIUSAGEMETER_SNAPSHOT_PATH";

    /// <summary>Where to write the still, or null when the app should start normally.</summary>
    public static string? RequestedPath => Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } path
        ? path
        : null;

    /// <summary>Which overlay size to render; defaults to medium.</summary>
    public static OverlaySize RequestedSize =>
        Enum.TryParse<OverlaySize>(Environment.GetEnvironmentVariable("AIUSAGEMETER_SNAPSHOT_SIZE"), true, out var size)
            ? size
            : OverlaySize.Medium;

    /// <summary>Whether to open a card, as <c>docs/aiusagemeter-demo.png</c> shows.</summary>
    public static bool RequestedCard =>
        Environment.GetEnvironmentVariable("AIUSAGEMETER_SNAPSHOT_CARD") is { Length: > 0 } value &&
        !value.Equals("0", StringComparison.Ordinal);

    /// <summary>Renders the overlay and writes it out. Returns where it went.</summary>
    public static string Capture(string path, OverlaySize size, bool withCard)
    {
        var providers = new[] { ProviderId.Claude, ProviderId.Codex, ProviderId.Grok, ProviderId.Copilot, ProviderId.Gemini, ProviderId.Cursor };
        var snapshots = DemoData.Snapshots(providers);

        var preferences = AppPreferences.Defaults with { OverlaySize = size };
        // Reduced motion: a still must not catch an animation part way through.
        var window = new OverlayWindow(preferences, new ImmediateScheduler(), reduced: true);
        window.Update(snapshots, new HashSet<ProviderId>());

        if (withCard)
        {
            window.Hover.Gauge(ProviderId.Claude, true);
            window.Present();
        }

        var surface = (FrameworkElement)window.Content;
        var width = window.PanelWidth;
        var height = window.PanelHeight;

        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();

        var target = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        target.Render(surface);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }
}
