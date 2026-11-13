using PadForge.Core.Input;
using PadForge.Core.Mapping;
using PadForge.Core.Runtime;

namespace PadForge.Core.Profiles;

public sealed class StickSettings
{
    public double Sensitivity { get; set; } = 1;

    public double Curve { get; set; } = 1.4;

    public double Deadzone { get; set; } = 0.06;

    public bool Spring { get; set; } = true;

    public double SpringRate { get; set; } = 26;
}

public sealed class Profile
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = "Default";

    public int HotkeyVirtualKey { get; set; } = VirtualKeyCodes.F8;

    public StickSettings RightStick { get; set; } = new();

    public Dictionary<string, int> Bindings { get; set; } = new(StringComparer.Ordinal);

    public int Binding(ControlTarget target)
    {
        return Bindings.TryGetValue(target.ToString(), out int virtualKey) ? virtualKey : 0;
    }

    public void SetBinding(ControlTarget target, int virtualKey)
    {
        if (virtualKey == 0)
            Bindings.Remove(target.ToString());
        else
            Bindings[target.ToString()] = virtualKey;
    }

    public ActiveSettings ToActive()
    {
        var keys = new int[ControlTargetCatalog.Count];
        foreach (ControlTarget target in Enum.GetValues<ControlTarget>())
            keys[(int)target] = Binding(target);

        return new ActiveSettings
        {
            RightStick = StickTuning.Clamp(
                RightStick.Sensitivity,
                RightStick.Curve,
                RightStick.Deadzone,
                RightStick.Spring,
                RightStick.SpringRate),
            HotkeyVirtualKey = HotkeyVirtualKey,
            Keys = keys,
        };
    }

    public static Profile CreateDefault()
    {
        var profile = new Profile();
        profile.SetBinding(ControlTarget.A, VirtualKeyCodes.Space);
        profile.SetBinding(ControlTarget.B, VirtualKeyCodes.C);
        profile.SetBinding(ControlTarget.X, VirtualKeyCodes.R);
        profile.SetBinding(ControlTarget.Y, VirtualKeyCodes.F);
        profile.SetBinding(ControlTarget.LeftShoulder, VirtualKeyCodes.Q);
        profile.SetBinding(ControlTarget.RightShoulder, VirtualKeyCodes.E);
        profile.SetBinding(ControlTarget.LeftTrigger, VirtualKeyCodes.MouseRight);
        profile.SetBinding(ControlTarget.RightTrigger, VirtualKeyCodes.MouseLeft);
        profile.SetBinding(ControlTarget.DpadUp, VirtualKeyCodes.Up);
        profile.SetBinding(ControlTarget.DpadDown, VirtualKeyCodes.Down);
        profile.SetBinding(ControlTarget.DpadLeft, VirtualKeyCodes.Left);
        profile.SetBinding(ControlTarget.DpadRight, VirtualKeyCodes.Right);
        profile.SetBinding(ControlTarget.Start, VirtualKeyCodes.Enter);
        profile.SetBinding(ControlTarget.Back, VirtualKeyCodes.Tab);
        profile.SetBinding(ControlTarget.LeftStick, VirtualKeyCodes.Z);
        profile.SetBinding(ControlTarget.RightStick, VirtualKeyCodes.MouseMiddle);
        profile.SetBinding(ControlTarget.LeftStickUp, VirtualKeyCodes.W);
        profile.SetBinding(ControlTarget.LeftStickDown, VirtualKeyCodes.S);
        profile.SetBinding(ControlTarget.LeftStickLeft, VirtualKeyCodes.A);
        profile.SetBinding(ControlTarget.LeftStickRight, VirtualKeyCodes.D);
        return profile;
    }
}
