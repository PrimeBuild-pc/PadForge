namespace PadForge.Core.Mapping;

public enum ControlTarget
{
    A = 0,
    B,
    X,
    Y,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    Start,
    Back,
    LeftStick,
    RightStick,
    LeftStickUp,
    LeftStickDown,
    LeftStickLeft,
    LeftStickRight,
}

public static class ControlTargetCatalog
{
    public static int Count { get; } = Enum.GetValues<ControlTarget>().Length;
}
