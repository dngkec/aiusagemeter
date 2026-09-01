using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AIUsageMeter.Windows.Interop;

/// <summary>
/// Window styles WPF has no property for.
/// </summary>
/// <remarks>
/// macOS gives the overlay a non-activating panel, so clicking a gauge never takes focus from
/// whatever the reader is working in. WPF's <c>ShowActivated</c> only governs the first show: every
/// later click activates the window and pulls focus out of the editor underneath. Only the extended
/// window style fixes that, and it can be set once the handle exists.
/// </remarks>
internal static class WindowStyles
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x0000_0080;
    private const int WsExNoActivate = 0x0800_0000;

    /// <summary>
    /// Stops the window ever taking focus, and keeps it out of Alt-Tab and the taskbar.
    /// </summary>
    /// <remarks>Call once the handle exists — from <c>SourceInitialized</c> onwards.</remarks>
    public static void MakeNonActivating(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("The window has no handle yet; wait for SourceInitialized.");

        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    /// <summary>True when the window will refuse focus. Reads the style back rather than assuming.</summary>
    public static bool IsNonActivating(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        return (GetWindowLong(handle, GwlExStyle) & WsExNoActivate) != 0;
    }

    // DllImport rather than LibraryImport: the source generator needs AllowUnsafeBlocks, and
    // turning that on for the whole application to save two marshalling stubs is a poor trade.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
