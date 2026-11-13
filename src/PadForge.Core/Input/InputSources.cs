namespace PadForge.Core.Input;

public interface IMouseSource
{
    (int X, int Y) ConsumeDelta();
}

public interface IKeyboardSource
{
    bool IsDown(int virtualKey);
}

/// <summary>
/// Win32 virtual-key codes used by the default profile. Values match the
/// Winuser.h constants so the WinUI layer can pass them through unchanged.
/// </summary>
public static class VirtualKeyCodes
{
    public const int MouseLeft = 0x01;
    public const int MouseRight = 0x02;
    public const int MouseMiddle = 0x04;
    public const int MouseX1 = 0x05;
    public const int MouseX2 = 0x06;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int A = 0x41;
    public const int C = 0x43;
    public const int D = 0x44;
    public const int E = 0x45;
    public const int F = 0x46;
    public const int Q = 0x51;
    public const int R = 0x52;
    public const int S = 0x53;
    public const int W = 0x57;
    public const int Z = 0x5A;
    public const int F8 = 0x77;
    public const int LeftShift = 0xA0;
    public const int LeftControl = 0xA2;

    public static bool IsMouseButton(int virtualKey) => virtualKey is >= MouseLeft and <= MouseX2;
}
