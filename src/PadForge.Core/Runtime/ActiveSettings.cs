using PadForge.Core.Mapping;
using PadForge.Core.Output;

namespace PadForge.Core.Runtime;

public sealed class ActiveSettings
{
    public required StickTuning RightStick { get; init; }

    public int HotkeyVirtualKey { get; init; }

    public float WalkScale { get; init; } = 0.5f;

    public bool HideInput { get; init; }

    public PadKind PadKind { get; init; }

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

    public PadKind PadKind { get; init; }

    public string? Fault { get; init; }

    public bool Calibrating { get; init; }

    public bool CalibrationReady { get; init; }

    public float CalibrationPeak { get; init; }

    public float CalibrationRemaining { get; init; }
}
