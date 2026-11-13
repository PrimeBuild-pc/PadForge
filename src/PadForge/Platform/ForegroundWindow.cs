using System.Runtime.InteropServices;

namespace PadForge.Platform;

internal static class ForegroundWindow
{
    public static bool IsForeground(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;

    /// <summary>Brings an already running PadForge window to the front.</summary>
    public static void ActivateExisting(string title)
    {
        IntPtr hwnd = FindWindowW(null, title);
        if (hwnd == IntPtr.Zero)
            return;

        ShowWindow(hwnd, 9);
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
