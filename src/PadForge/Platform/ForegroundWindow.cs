using System.Runtime.InteropServices;

namespace PadForge.Platform;

internal static class ForegroundWindow
{
    public static bool IsForeground(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
