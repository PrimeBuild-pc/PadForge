using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PadForge.Core.Mapping;

namespace PadForge.Controls;

public sealed partial class ControllerPreview : UserControl
{
    private const double Travel = 50;
    private Brush? _idle;
    private Brush? _hot;
    private Brush? _line;
    private Brush? _text;
    private Brush? _ink;

    public ControllerPreview()
    {
        InitializeComponent();
    }

    public void Apply(PadFrame frame)
    {
        EnsureBrushes();
        LeftShift.X = frame.LeftX * Travel;
        LeftShift.Y = -frame.LeftY * Travel;
        RightShift.X = frame.RightX * Travel;
        RightShift.Y = -frame.RightY * Travel;

        LtFill.Width = frame.LeftTrigger * LtTrack.ActualWidth;
        RtFill.Width = frame.RightTrigger * RtTrack.ActualWidth;

        Paint(AButton, ALabel, frame.Buttons.HasFlag(GamepadButtons.A));
        Paint(BButton, BLabel, frame.Buttons.HasFlag(GamepadButtons.B));
        Paint(XButton, XLabel, frame.Buttons.HasFlag(GamepadButtons.X));
        Paint(YButton, YLabel, frame.Buttons.HasFlag(GamepadButtons.Y));
        Paint(LbButton, LbLabel, frame.Buttons.HasFlag(GamepadButtons.LeftShoulder));
        Paint(RbButton, RbLabel, frame.Buttons.HasFlag(GamepadButtons.RightShoulder));
        Paint(StartButton, StartLabel, frame.Buttons.HasFlag(GamepadButtons.Start));
        Paint(BackButton, BackLabel, frame.Buttons.HasFlag(GamepadButtons.Back));
        Paint(PadUp, null, frame.Buttons.HasFlag(GamepadButtons.DpadUp));
        Paint(PadDown, null, frame.Buttons.HasFlag(GamepadButtons.DpadDown));
        Paint(PadLeft, null, frame.Buttons.HasFlag(GamepadButtons.DpadLeft));
        Paint(PadRight, null, frame.Buttons.HasFlag(GamepadButtons.DpadRight));

        LeftClickRing.Stroke = frame.Buttons.HasFlag(GamepadButtons.LeftStick) ? _hot : _line;
        RightClickRing.Stroke = frame.Buttons.HasFlag(GamepadButtons.RightStick) ? _hot : _line;
        LeftClickRing.StrokeThickness = frame.Buttons.HasFlag(GamepadButtons.LeftStick) ? 3 : 2;
        RightClickRing.StrokeThickness = frame.Buttons.HasFlag(GamepadButtons.RightStick) ? 3 : 2;

        Readout.Text =
            $"L {frame.LeftX,6:+0.00;-0.00; 0.00} {frame.LeftY,6:+0.00;-0.00; 0.00}      R {frame.RightX,6:+0.00;-0.00; 0.00} {frame.RightY,6:+0.00;-0.00; 0.00}";
    }

    private void Paint(Border box, TextBlock? label, bool pressed)
    {
        box.Background = pressed ? _hot : _idle;
        box.BorderBrush = pressed ? _hot : _line;
        if (label is not null)
            label.Foreground = pressed ? _ink : _text;
    }

    private void EnsureBrushes()
    {
        if (_idle is not null)
            return;

        _idle = Brush("ForgeButtonBrush");
        _hot = Brush("ForgeAccentBrush");
        _line = Brush("ForgeCardBorderBrush");
        _text = Brush("ForgeTextBrush");
        _ink = Brush("ForgeAccentDeepBrush");
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
