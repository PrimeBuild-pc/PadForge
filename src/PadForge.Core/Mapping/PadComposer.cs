using PadForge.Core.Runtime;

namespace PadForge.Core.Mapping;

public readonly record struct ComposedInput(
    float LeftX,
    float LeftY,
    float RightX,
    float RightY,
    byte LeftTrigger,
    byte RightTrigger,
    GamepadButtons Buttons,
    float RightRaw = 0f)
{
    public PadFrame ToFrame() => new()
    {
        LeftX = LeftX,
        LeftY = LeftY,
        RightX = RightX,
        RightY = RightY,
        LeftTrigger = LeftTrigger / 255f,
        RightTrigger = RightTrigger / 255f,
        Buttons = Buttons,
        RightRaw = RightRaw,
    };

    public GamepadReport ToReport() => new(
        AxisMath.ToAxis(LeftX),
        AxisMath.ToAxis(LeftY),
        AxisMath.ToAxis(RightX),
        AxisMath.ToAxis(RightY),
        LeftTrigger,
        RightTrigger,
        Buttons);
}

public static class PadComposer
{
    public static ComposedInput Compose(
        MouseToStickConverter rightStick,
        ActiveSettings settings,
        int mouseDeltaX,
        int mouseDeltaY,
        Func<int, bool> isKeyDown,
        float deltaSeconds)
    {
        var (rightX, rightY) = rightStick.Update(mouseDeltaX, mouseDeltaY, settings.RightStick, deltaSeconds);

        bool Down(ControlTarget target)
        {
            int virtualKey = settings.Key(target);
            return virtualKey != 0 && isKeyDown(virtualKey);
        }

        float leftX = (Down(ControlTarget.LeftStickRight) ? 1f : 0f) - (Down(ControlTarget.LeftStickLeft) ? 1f : 0f);
        float leftY = (Down(ControlTarget.LeftStickUp) ? 1f : 0f) - (Down(ControlTarget.LeftStickDown) ? 1f : 0f);
        float leftMagnitude = MathF.Sqrt((leftX * leftX) + (leftY * leftY));
        if (leftMagnitude > 1f)
        {
            leftX /= leftMagnitude;
            leftY /= leftMagnitude;
        }

        if (Down(ControlTarget.LeftStickWalk))
        {
            leftX *= settings.WalkScale;
            leftY *= settings.WalkScale;
        }

        GamepadButtons buttons = GamepadButtons.None;
        if (Down(ControlTarget.A)) buttons |= GamepadButtons.A;
        if (Down(ControlTarget.B)) buttons |= GamepadButtons.B;
        if (Down(ControlTarget.X)) buttons |= GamepadButtons.X;
        if (Down(ControlTarget.Y)) buttons |= GamepadButtons.Y;
        if (Down(ControlTarget.LeftShoulder)) buttons |= GamepadButtons.LeftShoulder;
        if (Down(ControlTarget.RightShoulder)) buttons |= GamepadButtons.RightShoulder;
        if (Down(ControlTarget.DpadUp)) buttons |= GamepadButtons.DpadUp;
        if (Down(ControlTarget.DpadDown)) buttons |= GamepadButtons.DpadDown;
        if (Down(ControlTarget.DpadLeft)) buttons |= GamepadButtons.DpadLeft;
        if (Down(ControlTarget.DpadRight)) buttons |= GamepadButtons.DpadRight;
        if (Down(ControlTarget.Start)) buttons |= GamepadButtons.Start;
        if (Down(ControlTarget.Back)) buttons |= GamepadButtons.Back;
        if (Down(ControlTarget.LeftStick)) buttons |= GamepadButtons.LeftStick;
        if (Down(ControlTarget.RightStick)) buttons |= GamepadButtons.RightStick;

        byte leftTrigger = Down(ControlTarget.LeftTrigger) ? (byte)255 : (byte)0;
        byte rightTrigger = Down(ControlTarget.RightTrigger) ? (byte)255 : (byte)0;

        return new ComposedInput(leftX, leftY, rightX, rightY, leftTrigger, rightTrigger, buttons, rightStick.RawMagnitude);
    }
}
