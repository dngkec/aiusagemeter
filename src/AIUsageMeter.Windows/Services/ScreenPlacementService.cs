using System.Windows.Forms;
using AIUsageMeter.Core;

namespace AIUsageMeter.Windows.Services;

internal static class ScreenPlacementService
{
    public static IReadOnlyList<(string Id, string Name)> Screens => Screen.AllScreens.Select((x, index) => (x.DeviceName, $"Display {index + 1} ({x.Bounds.Width}×{x.Bounds.Height})")).ToList();

    public static void Place(OverlayWindow window, AppPreferences preferences)
    {
        var screen = preferences.ScreenIdentifier is { Length: > 0 } id ? Screen.AllScreens.FirstOrDefault(x => x.DeviceName == id) : null;
        screen ??= Screen.FromPoint(Cursor.Position);
        var dpi = GetDpi(screen);
        var scale = dpi / 96d;
        var area = screen.WorkingArea;
        var desiredHeight = Math.Min(window.DesiredSize.Height > 0 ? window.DesiredSize.Height : window.ActualHeight, area.Height / scale);
        var width = preferences.OverlaySize switch { OverlaySize.Compact => 320d, OverlaySize.Large => 430d, _ => 382d };
        var frame = OverlayLayout.Place(new(area.Left / scale, area.Top / scale, area.Width / scale, area.Height / scale), width, Math.Max(80, desiredHeight), preferences.VerticalPosition, preferences.VerticalOffset);
        window.Width = frame.Width; window.Left = frame.X; window.Top = frame.Y;
    }

    private static uint GetDpi(Screen screen)
    {
        var point = new NativePoint { X = screen.Bounds.Left + 1, Y = screen.Bounds.Top + 1 };
        var monitor = MonitorFromPoint(point, 2);
        return GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? x : 96;
    }

    private struct NativePoint { public int X; public int Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [System.Runtime.InteropServices.DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
