using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using PadForge.Core.Mapping;
using PadForge.Core.Output;
using IVirtualGamepad = PadForge.Core.Output.IVirtualGamepad;

namespace PadForge.Output;

/// <summary>A ViGEm virtual pad, either an Xbox 360 or a DualShock 4.</summary>
public sealed class VigemGamepad : IVirtualGamepad
{
    private ViGEmClient? _client;
    private IXbox360Controller? _xbox;
    private IDualShock4Controller? _ds4;
    private bool _connected;
    private bool _disposed;

    public bool IsConnected => _connected;

    public PadKind Kind { get; set; }

    public void Connect()
    {
        if (_connected)
            return;

        try
        {
            _client ??= new ViGEmClient();
            if (Kind == PadKind.DualShock4)
            {
                IDualShock4Controller pad = _client.CreateDualShock4Controller();
                pad.AutoSubmitReport = false;
                pad.Connect();
                _ds4 = pad;
            }
            else
            {
                IXbox360Controller pad = _client.CreateXbox360Controller();
                pad.AutoSubmitReport = false;
                pad.Connect();
                _xbox = pad;
            }

            _connected = true;
        }
        catch (VigemBusNotFoundException)
        {
            throw new VirtualGamepadException("ViGEmBus was not found. Install the driver and try again.");
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            throw new VirtualGamepadException("Could not create the virtual controller.");
        }
    }

    public void Disconnect()
    {
        if (!_connected)
            return;

        try
        {
            Submit(GamepadReport.Neutral);
            _xbox?.Disconnect();
            _ds4?.Disconnect();
        }
        finally
        {
            _xbox = null;
            _ds4 = null;
            _connected = false;
        }
    }

    public void Submit(in GamepadReport report)
    {
        if (!_connected)
            return;

        if (_xbox is { } xbox)
        {
            xbox.LeftThumbX = report.LeftX;
            xbox.LeftThumbY = report.LeftY;
            xbox.RightThumbX = report.RightX;
            xbox.RightThumbY = report.RightY;
            xbox.LeftTrigger = report.LeftTrigger;
            xbox.RightTrigger = report.RightTrigger;
            xbox.ButtonState = (ushort)report.Buttons;
            xbox.SubmitReport();
        }
        else if (_ds4 is { } ds4)
        {
            SubmitDualShock4(ds4, report);
        }
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

    private static void SubmitDualShock4(IDualShock4Controller pad, in GamepadReport report)
    {
        GamepadButtons b = report.Buttons;
        pad.SetAxisValue(DualShock4Axis.LeftThumbX, DualShock4Mapping.Axis(report.LeftX));
        pad.SetAxisValue(DualShock4Axis.LeftThumbY, DualShock4Mapping.Axis(report.LeftY, invert: true));
        pad.SetAxisValue(DualShock4Axis.RightThumbX, DualShock4Mapping.Axis(report.RightX));
        pad.SetAxisValue(DualShock4Axis.RightThumbY, DualShock4Mapping.Axis(report.RightY, invert: true));
        pad.SetSliderValue(DualShock4Slider.LeftTrigger, report.LeftTrigger);
        pad.SetSliderValue(DualShock4Slider.RightTrigger, report.RightTrigger);
        pad.SetButtonState(DualShock4Button.Cross, b.HasFlag(GamepadButtons.A));
        pad.SetButtonState(DualShock4Button.Circle, b.HasFlag(GamepadButtons.B));
        pad.SetButtonState(DualShock4Button.Square, b.HasFlag(GamepadButtons.X));
        pad.SetButtonState(DualShock4Button.Triangle, b.HasFlag(GamepadButtons.Y));
        pad.SetButtonState(DualShock4Button.ShoulderLeft, b.HasFlag(GamepadButtons.LeftShoulder));
        pad.SetButtonState(DualShock4Button.ShoulderRight, b.HasFlag(GamepadButtons.RightShoulder));
        pad.SetButtonState(DualShock4Button.TriggerLeft, report.LeftTrigger > 0);
        pad.SetButtonState(DualShock4Button.TriggerRight, report.RightTrigger > 0);
        pad.SetButtonState(DualShock4Button.Share, b.HasFlag(GamepadButtons.Back));
        pad.SetButtonState(DualShock4Button.Options, b.HasFlag(GamepadButtons.Start));
        pad.SetButtonState(DualShock4Button.ThumbLeft, b.HasFlag(GamepadButtons.LeftStick));
        pad.SetButtonState(DualShock4Button.ThumbRight, b.HasFlag(GamepadButtons.RightStick));
        pad.SetDPadDirection(DualShock4Mapping.Hat(b) switch
        {
            0 => DualShock4DPadDirection.North,
            1 => DualShock4DPadDirection.Northeast,
            2 => DualShock4DPadDirection.East,
            3 => DualShock4DPadDirection.Southeast,
            4 => DualShock4DPadDirection.South,
            5 => DualShock4DPadDirection.Southwest,
            6 => DualShock4DPadDirection.West,
            7 => DualShock4DPadDirection.Northwest,
            _ => DualShock4DPadDirection.None,
        });
        pad.SubmitReport();
    }
}
