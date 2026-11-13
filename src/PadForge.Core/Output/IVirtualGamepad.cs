using PadForge.Core.Mapping;

namespace PadForge.Core.Output;

public interface IVirtualGamepad : IDisposable
{
    bool IsConnected { get; }

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
