using PadForge.Core.Mapping;

namespace PadForge.Core.Runtime;

public sealed class ActiveSettings
{
    public required StickTuning RightStick { get; init; }

    public int HotkeyVirtualKey { get; init; }

    public required int[] Keys { get; init; }

    public int Key(ControlTarget target)
    {
        int index = (int)target;
        if ((uint)index >= (uint)Keys.Length)
            return 0;

        return Keys[index];
    }
}

public sealed class EngineSnapshot
{
    public PadFrame Frame { get; init; } = PadFrame.Neutral;

    public bool BusKnown { get; init; }

    public bool BusReady { get; init; }

    public bool Connected { get; init; }

    public bool Sending { get; init; }

    public string? Fault { get; init; }
}
