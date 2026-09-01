using System.Windows.Forms;
using AIUsageMeter.Core;
using AIUsageMeter.Windows.Overlay;

using Screen = System.Windows.Forms.Screen;

namespace AIUsageMeter.Windows.Services;

/// <summary>
/// Puts the overlay against the trailing edge of the chosen display.
/// </summary>
/// <remarks>
/// <para>
/// The tricky part is units. <c>Window.Left</c> and <c>Top</c> are in the DIPs of the process's
/// awareness root — for a per-monitor-aware app, the primary display's scale — while
/// <c>Screen.WorkingArea</c> is in physical pixels. Converting the target monitor's work area with
/// the target monitor's own scale, as this did before, lands the overlay somewhere else entirely on
/// a mixed-scale desktop: a 150% laptop screen beside a 100% external one put it hundreds of points
/// off. Both conversions have to use the same scale, and that is the primary's.
/// </para>
/// </remarks>
internal static class ScreenPlacementService
{
    public static IReadOnlyList<(string Id, string Name)> Screens =>
        Screen.AllScreens.Select((x, index) => (x.DeviceName, $"Display {index + 1} ({x.Bounds.Width}×{x.Bounds.Height})")).ToList();

    public static void Place(OverlayWindow window, AppPreferences preferences)
    {
        var screen = Target(preferences);
        var scale = PrimaryScale();
        var area = screen.WorkingArea;
        var work = new MeterRect(area.Left / scale, area.Top / scale, area.Width / scale, area.Height / scale);

        var frame = window.Hover.IdleMini
            ? OverlayLayout.MiniFrame(work, window.PanelWidth, window.PanelHeight,
                window.Rail.NaturalHeight, preferences.VerticalPosition, preferences.VerticalOffset)
            : OverlayLayout.PanelFrame(work, window.PanelWidth, window.Rail.NaturalHeight,
                window.PanelHeight, preferences.VerticalPosition, preferences.VerticalOffset);

        window.Width = frame.Width;
        window.Height = frame.Height;
        window.Left = frame.X;
        window.Top = frame.Y;
    }

    private static Screen Target(AppPreferences preferences)
    {
        if (preferences.ScreenIdentifier is { Length: > 0 } id &&
            Screen.AllScreens.FirstOrDefault(x => x.DeviceName == id) is { } chosen)
            return chosen;

        return Screen.FromPoint(Cursor.Position);
    }

    /// <summary>
    /// The scale WPF measures <c>Window.Left</c> and <c>Top</c> in.
    /// </summary>
    /// <remarks>
    /// Read from the primary display, whatever monitor the overlay is bound for, because that is the
    /// space those properties live in.
    /// </remarks>
    private static double PrimaryScale()
    {
        var primary = Screen.PrimaryScreen;
        if (primary is null) return 1;

        var point = new NativePoint { X = primary.Bounds.Left + 1, Y = primary.Bounds.Top + 1 };
        const uint nearest = 2;
        var monitor = MonitorFromPoint(point, nearest);
        return GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96d : 1;
    }

    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
