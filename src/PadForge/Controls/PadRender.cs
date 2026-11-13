using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using PadForge.Core.Mapping;
using PadForge.Core.Output;
using Windows.Foundation;
using Windows.UI;

namespace PadForge.Controls;

/// <summary>
/// A pre-rendered controller (1280x720) with live overlays: pressed controls glow and the
/// stick caps follow the sticks. Anchors are in the same pixel space, for callout lines.
/// </summary>
public sealed class PadRender : Grid
{
    public const double RenderWidth = 1280;
    public const double RenderHeight = 720;
    private const double StickTravel = 22;

    private static readonly Dictionary<ControlTarget, Point> XboxAnchors = new()
    {
        [ControlTarget.LeftTrigger] = new(440, 100),
        [ControlTarget.RightTrigger] = new(840, 100),
        [ControlTarget.LeftShoulder] = new(360, 124),
        [ControlTarget.RightShoulder] = new(920, 124),
        [ControlTarget.Back] = new(547, 241),
        [ControlTarget.Start] = new(683, 241),
        [ControlTarget.LeftStick] = new(398, 235),
        [ControlTarget.RightStick] = new(731, 358),
        [ControlTarget.Y] = new(827, 191),
        [ControlTarget.X] = new(769, 246),
        [ControlTarget.B] = new(893, 246),
        [ControlTarget.A] = new(829, 300),
        [ControlTarget.DpadUp] = new(493, 323),
        [ControlTarget.DpadDown] = new(493, 402),
        [ControlTarget.DpadLeft] = new(453, 362),
        [ControlTarget.DpadRight] = new(533, 362),
    };

    private static readonly Dictionary<ControlTarget, Point> DualShockAnchors = new()
    {
        [ControlTarget.LeftTrigger] = new(255, 140),
        [ControlTarget.RightTrigger] = new(1025, 140),
        [ControlTarget.LeftShoulder] = new(330, 128),
        [ControlTarget.RightShoulder] = new(950, 128),
        [ControlTarget.Back] = new(417, 176),
        [ControlTarget.Start] = new(784, 176),
        [ControlTarget.LeftStick] = new(451, 378),
        [ControlTarget.RightStick] = new(753, 378),
        [ControlTarget.Y] = new(880, 197),
        [ControlTarget.X] = new(816, 258),
        [ControlTarget.B] = new(947, 260),
        [ControlTarget.A] = new(880, 320),
        [ControlTarget.DpadUp] = new(317, 210),
        [ControlTarget.DpadDown] = new(317, 307),
        [ControlTarget.DpadLeft] = new(265, 257),
        [ControlTarget.DpadRight] = new(368, 257),
    };

    private static readonly (ControlTarget Target, GamepadButtons Button, double Radius)[] Glows =
    [
        (ControlTarget.A, GamepadButtons.A, 24),
        (ControlTarget.B, GamepadButtons.B, 24),
        (ControlTarget.X, GamepadButtons.X, 24),
        (ControlTarget.Y, GamepadButtons.Y, 24),
        (ControlTarget.LeftShoulder, GamepadButtons.LeftShoulder, 30),
        (ControlTarget.RightShoulder, GamepadButtons.RightShoulder, 30),
        (ControlTarget.Back, GamepadButtons.Back, 16),
        (ControlTarget.Start, GamepadButtons.Start, 16),
        (ControlTarget.DpadUp, GamepadButtons.DpadUp, 20),
        (ControlTarget.DpadDown, GamepadButtons.DpadDown, 20),
        (ControlTarget.DpadLeft, GamepadButtons.DpadLeft, 20),
        (ControlTarget.DpadRight, GamepadButtons.DpadRight, 20),
    ];

    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Canvas _overlay = new() { Width = RenderWidth, Height = RenderHeight };
    private readonly Dictionary<ControlTarget, Ellipse> _glow = new();
    private readonly Dictionary<ControlTarget, (Border Disc, TextBlock Glyph)> _faces = new();
    private readonly Ellipse _leftCap = Cap();
    private readonly Ellipse _rightCap = Cap();
    private readonly TranslateTransform _leftShift = new();
    private readonly TranslateTransform _rightShift = new();
    private readonly Ellipse _ltGlow = Glow(34);
    private readonly Ellipse _rtGlow = Glow(34);
    private PadKind? _kind;

    public PadRender()
    {
        Width = RenderWidth;
        Height = RenderHeight;
        Children.Add(_image);
        Children.Add(_overlay);
        _leftCap.RenderTransform = _leftShift;
        _rightCap.RenderTransform = _rightShift;
        _overlay.Children.Add(_ltGlow);
        _overlay.Children.Add(_rtGlow);
        foreach (var (target, _, radius) in Glows)
        {
            Ellipse glow = Glow(radius);
            _glow[target] = glow;
            _overlay.Children.Add(glow);
        }

        foreach (ControlTarget face in new[] { ControlTarget.A, ControlTarget.B, ControlTarget.X, ControlTarget.Y })
        {
            var glyph = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var disc = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = new SolidColorBrush(Color.FromArgb(255, 30, 31, 38)), BorderThickness = new Thickness(1.5), Child = glyph };
            _faces[face] = (disc, glyph);
            _overlay.Children.Add(disc);
        }

        _overlay.Children.Add(_leftCap);
        _overlay.Children.Add(_rightCap);
        SetKind(PadKind.Xbox360);
    }

    public static IReadOnlyDictionary<ControlTarget, Point> AnchorsFor(PadKind kind)
    {
        var anchors = kind == PadKind.DualShock4 ? DualShockAnchors : XboxAnchors;
        var all = new Dictionary<ControlTarget, Point>(anchors);
        Point stick = anchors[ControlTarget.LeftStick];
        all[ControlTarget.LeftStickUp] = new(stick.X, stick.Y - 30);
        all[ControlTarget.LeftStickDown] = new(stick.X, stick.Y + 30);
        all[ControlTarget.LeftStickLeft] = new(stick.X - 30, stick.Y);
        all[ControlTarget.LeftStickRight] = new(stick.X + 30, stick.Y);
        all[ControlTarget.LeftStickWalk] = new(stick.X - 20, stick.Y + 22);
        return all;
    }

    public void SetKind(PadKind kind)
    {
        if (_kind == kind)
            return;

        _kind = kind;
        string file = kind == PadKind.DualShock4 ? "dualshock4.png" : "xbox360.png";
        _image.Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Pads", file)));
        IReadOnlyDictionary<ControlTarget, Point> anchors = AnchorsFor(kind);
        foreach (var (target, glow) in _glow)
            Place(glow, anchors[target]);

        Place(_ltGlow, anchors[ControlTarget.LeftTrigger]);
        Place(_rtGlow, anchors[ControlTarget.RightTrigger]);
        Place(_leftCap, anchors[ControlTarget.LeftStick]);
        Place(_rightCap, anchors[ControlTarget.RightStick]);

        bool ps = kind == PadKind.DualShock4;
        Face(ControlTarget.A, anchors, ps ? "✕" : "A", ps ? Color.FromArgb(255, 120, 160, 255) : Color.FromArgb(255, 108, 214, 138));
        Face(ControlTarget.B, anchors, ps ? "○" : "B", Color.FromArgb(255, 240, 112, 112));
        Face(ControlTarget.X, anchors, ps ? "□" : "X", ps ? Color.FromArgb(255, 230, 130, 200) : Color.FromArgb(255, 102, 160, 255));
        Face(ControlTarget.Y, anchors, ps ? "△" : "Y", ps ? Color.FromArgb(255, 108, 214, 170) : Color.FromArgb(255, 240, 200, 90));
    }

    public void Apply(PadFrame frame)
    {
        foreach (var (target, button, _) in Glows)
            _glow[target].Opacity = frame.Buttons.HasFlag(button) ? 0.85 : 0;

        _ltGlow.Opacity = frame.LeftTrigger * 0.85;
        _rtGlow.Opacity = frame.RightTrigger * 0.85;
        _leftShift.X = frame.LeftX * StickTravel;
        _leftShift.Y = -frame.LeftY * StickTravel;
        _rightShift.X = frame.RightX * StickTravel;
        _rightShift.Y = -frame.RightY * StickTravel;
        bool leftClick = frame.Buttons.HasFlag(GamepadButtons.LeftStick);
        bool rightClick = frame.Buttons.HasFlag(GamepadButtons.RightStick);
        _leftCap.Opacity = leftClick || Math.Abs(frame.LeftX) + Math.Abs(frame.LeftY) > 0.04 ? 1 : 0.35;
        _rightCap.Opacity = rightClick || Math.Abs(frame.RightX) + Math.Abs(frame.RightY) > 0.04 ? 1 : 0.35;
        _leftCap.StrokeThickness = leftClick ? 7 : 3;
        _rightCap.StrokeThickness = rightClick ? 7 : 3;
    }

    private void Face(ControlTarget target, IReadOnlyDictionary<ControlTarget, Point> anchors, string text, Color tint)
    {
        var (disc, glyph) = _faces[target];
        glyph.Text = text;
        glyph.Foreground = new SolidColorBrush(tint);
        disc.BorderBrush = new SolidColorBrush(Color.FromArgb(90, tint.R, tint.G, tint.B));
        Canvas.SetLeft(disc, anchors[target].X - 20);
        Canvas.SetTop(disc, anchors[target].Y - 20);
    }

    private static void Place(FrameworkElement element, Point at)
    {
        Canvas.SetLeft(element, at.X - (element.Width / 2));
        Canvas.SetTop(element, at.Y - (element.Height / 2));
    }

    private static Ellipse Glow(double radius) => new()
    {
        Width = radius * 2,
        Height = radius * 2,
        Opacity = 0,
        IsHitTestVisible = false,
        Fill = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(230, 255, 138, 61), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(150, 255, 138, 61), Offset = 0.6 },
                new GradientStop { Color = Colors.Transparent, Offset = 1 },
            },
        },
    };

    private static Ellipse Cap() => new()
    {
        Width = 58,
        Height = 58,
        Opacity = 0.35,
        IsHitTestVisible = false,
        Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 138, 61)),
        StrokeThickness = 3,
    };
}
