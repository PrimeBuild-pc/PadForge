using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using PadForge.Core.Mapping;
using PadForge.Core.Output;
using IVirtualGamepad = PadForge.Core.Output.IVirtualGamepad;

namespace PadForge.Output;

public sealed class VigemXboxGamepad : IVirtualGamepad
{
    private ViGEmClient? _client;
    private IXbox360Controller? _pad;
    private bool _connected;
    private bool _disposed;

    public bool IsConnected => _connected;

    public void Connect()
    {
        if (_connected)
            return;

        try
        {
            _client ??= new ViGEmClient();
            IXbox360Controller pad = _client.CreateXbox360Controller();
            pad.AutoSubmitReport = false;
            pad.Connect();
            _pad = pad;
            _connected = true;
        }
        catch (VigemBusNotFoundException)
        {
            throw new VirtualGamepadException("ViGEmBus was not found. Install the driver and try again.");
        }
        catch (VirtualGamepadException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            throw new VirtualGamepadException("Could not create the virtual controller.");
        }
    }

    public void Disconnect()
    {
        if (!_connected || _pad is null)
            return;

        try
        {
            Submit(GamepadReport.Neutral);
            _pad.Disconnect();
        }
        finally
        {
            _pad = null;
            _connected = false;
        }
    }

    public void Submit(in GamepadReport report)
    {
        IXbox360Controller? pad = _pad;
        if (!_connected || pad is null)
            return;

        pad.LeftThumbX = report.LeftX;
        pad.LeftThumbY = report.LeftY;
        pad.RightThumbX = report.RightX;
        pad.RightThumbY = report.RightY;
        pad.LeftTrigger = report.LeftTrigger;
        pad.RightTrigger = report.RightTrigger;
        pad.ButtonState = (ushort)report.Buttons;
        pad.SubmitReport();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            Disconnect();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }

        _client?.Dispose();
        _client = null;
    }
}
