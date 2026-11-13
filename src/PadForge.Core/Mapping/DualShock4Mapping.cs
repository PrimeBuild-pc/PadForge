namespace PadForge.Core.Mapping;

/// <summary>Converts the Xbox-style report into DualShock 4 units.</summary>
public static class DualShock4Mapping
{
    /// <summary>Signed 16-bit axis to a 0..255 byte with 128 at rest. DS4 Y grows downward.</summary>
    public static byte Axis(short value, bool invert = false)
    {
        if (value == 0)
            return 128;

        int scaled = (value + 32768) >> 8;
        return (byte)Math.Clamp(invert ? 255 - scaled : scaled, 0, 255);
    }

    /// <summary>D-pad buttons to a hat value: 0 is north, clockwise to 7; -1 is released.</summary>
    public static int Hat(GamepadButtons buttons)
    {
        bool up = buttons.HasFlag(GamepadButtons.DpadUp);
        bool down = buttons.HasFlag(GamepadButtons.DpadDown);
        bool left = buttons.HasFlag(GamepadButtons.DpadLeft);
        bool right = buttons.HasFlag(GamepadButtons.DpadRight);
        if (up && down)
            (up, down) = (false, false);
        if (left && right)
            (left, right) = (false, false);

        return (up, right, down, left) switch
        {
            (true, false, false, false) => 0,
            (true, true, false, false) => 1,
            (false, true, false, false) => 2,
            (false, true, true, false) => 3,
            (false, false, true, false) => 4,
            (false, false, true, true) => 5,
            (false, false, false, true) => 6,
            (true, false, false, true) => 7,
            _ => -1,
        };
    }
}
