using System.Runtime.InteropServices;

namespace PadForge.Platform;

internal static class TimerResolution
{
    private static bool _active;

    public static void Begin()
    {
        if (_active)
            return;

        if (timeBeginPeriod(1) == 0)
            _active = true;
    }

    public static void End()
    {
        if (!_active)
            return;

        timeEndPeriod(1);
        _active = false;
    }

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeEndPeriod(uint period);
}
