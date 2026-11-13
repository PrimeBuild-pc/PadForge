using PadForge.Core.Mapping;

namespace PadForge.Core.Output;

/// <summary>The controller Windows and the game see.</summary>
public enum PadKind
{
    Xbox360 = 0,
    DualShock4 = 1,
}

public interface IVirtualGamepad : IDisposable
{
    bool IsConnected { get; }

    /// <summary>Takes effect on the next <see cref="Connect"/>.</summary>
    PadKind Kind { get; set; }

    void Connect();

    void Disconnect();

    void Submit(in GamepadReport report);
}

public sealed class VirtualGamepadException : Exception
{
    public VirtualGamepadException(string message) : base(message)
    {
    }
}
