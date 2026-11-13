namespace PadForge.Core.Mapping;

/// <summary>
/// Bit values match the XUSB report so a virtual Xbox 360 pad can take the
/// mask in one write. Guide is intentionally absent.
/// </summary>
[Flags]
public enum GamepadButtons : ushort
{
    None = 0,
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftStick = 0x0040,
    RightStick = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

public readonly record struct GamepadReport(
    short LeftX,
    short LeftY,
    short RightX,
    short RightY,
    byte LeftTrigger,
    byte RightTrigger,
    GamepadButtons Buttons)
{
    public static GamepadReport Neutral => default;
}

/// <summary>
/// Stick and trigger state in UI units. Sticks are -1..1, triggers are 0..1.
/// Replaced as a whole object so the UI thread can read it without tearing.
/// </summary>
public sealed class PadFrame
{
    public static PadFrame Neutral { get; } = new();

    public float LeftX { get; init; }
    public float LeftY { get; init; }
    public float RightX { get; init; }
    public float RightY { get; init; }
    public float LeftTrigger { get; init; }
    public float RightTrigger { get; init; }
    public GamepadButtons Buttons { get; init; }

    public float RightRaw { get; init; }
}

public static class AxisMath
{
    public static short ToAxis(float value)
    {
        float clamped = Math.Clamp(value, -1f, 1f);
        if (clamped >= 0f)
            return (short)MathF.Round(clamped * 32767f);

        return (short)MathF.Round(clamped * 32768f);
    }
}
