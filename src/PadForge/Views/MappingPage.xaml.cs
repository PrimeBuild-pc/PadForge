using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PadForge.Controls;
using PadForge.Core.Mapping;
using PadForge.Core.Output;
using PadForge.ViewModels;
using Windows.Foundation;

namespace PadForge.Views;

public sealed partial class MappingPage : UserControl, IFramePage
{
    private const double PadLeft = 200;
    private const double PadTop = 96;
    private const double PadScale = 600 / PadRender.RenderWidth;
    private const double BoxWidth = 176;
    private const double BoxHeight = 48;

    private static readonly ControlTarget[] LeftColumn =
    [
        ControlTarget.LeftTrigger, ControlTarget.LeftShoulder, ControlTarget.Back,
        ControlTarget.LeftStickUp, ControlTarget.LeftStickDown, ControlTarget.LeftStickLeft,
        ControlTarget.LeftStickRight, ControlTarget.LeftStickWalk, ControlTarget.LeftStick,
    ];

    private static readonly ControlTarget[] RightColumn =
    [
        ControlTarget.RightTrigger, ControlTarget.RightShoulder, ControlTarget.Start,
        ControlTarget.Y, ControlTarget.B, ControlTarget.X, ControlTarget.A, ControlTarget.RightStick,
    ];

    private static readonly ControlTarget[] BottomRow =
    [
        ControlTarget.DpadUp, ControlTarget.DpadDown, ControlTarget.DpadLeft, ControlTarget.DpadRight,
    ];

    private readonly PadRender _pad = new();
    private readonly List<Callout> _callouts = new();
    private readonly Brush _line = Res("ForgeCardBorderBrush");
    private readonly Brush _hot = Res("ForgeAccentBrush");
    private readonly Brush _text = Res("ForgeTextBrush");
    private readonly Brush _subtle = Res("ForgeSubtleBrush");
    private PadKind? _built;

    public MappingPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _pad.RenderTransform = new ScaleTransform { ScaleX = PadScale, ScaleY = PadScale };
        Canvas.SetLeft(_pad, PadLeft);
        Canvas.SetTop(_pad, PadTop);
        Build(viewModel.PadKind);
    }

    public MainViewModel ViewModel { get; }

    public void Render(MainViewModel viewModel)
    {
        if (viewModel.PadKind != _built)
            Build(viewModel.PadKind);

        PadFrame frame = viewModel.Frame;
        _pad.Apply(frame);
        foreach (Callout callout in _callouts)
        {
            bool live = IsLive(callout.Row.Target, frame);
            if (live == callout.Live)
                continue;

            callout.Live = live;
            Paint(callout);
        }
    }

    private void Build(PadKind kind)
    {
        foreach (Callout old in _callouts)
            old.Row.PropertyChanged -= old.OnRowChanged;

        _callouts.Clear();
        Board.Children.Clear();
        _built = kind;
        _pad.SetKind(kind);

        var lines = new Canvas();
        Board.Children.Add(_pad);
        Board.Children.Add(lines);
        IReadOnlyDictionary<ControlTarget, Point> anchors = PadRender.AnchorsFor(kind);

        for (int i = 0; i < LeftColumn.Length; i++)
            Add(lines, anchors, LeftColumn[i], new Point(16, 24 + (i * 56)), Side.Right);

        for (int i = 0; i < RightColumn.Length; i++)
            Add(lines, anchors, RightColumn[i], new Point(1000 - 16 - BoxWidth, 24 + (i * 56)), Side.Left);

        const double bottomWidth = 112;
        double start = 500 - (((BottomRow.Length * bottomWidth) + ((BottomRow.Length - 1) * 10)) / 2);
        for (int i = 0; i < BottomRow.Length; i++)
            Add(lines, anchors, BottomRow[i], new Point(start + (i * (bottomWidth + 10)), 536), Side.Top, bottomWidth);
    }

    private void Add(Canvas lines, IReadOnlyDictionary<ControlTarget, Point> anchors, ControlTarget target, Point at, Side anchor, double width = BoxWidth)
    {
        BindingRow row = ViewModel.Row(target);
        var title = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _text, TextTrimming = TextTrimming.CharacterEllipsis };
        var key = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["CalloutButton"],
            Width = width,
            Height = BoxHeight,
            Content = new StackPanel { Spacing = 1, Children = { title, key } },
            Command = row.CaptureCommand,
        };
        Canvas.SetLeft(button, at.X);
        Canvas.SetTop(button, at.Y);
        Board.Children.Add(button);

        Point from = anchor switch
        {
            Side.Right => new Point(at.X + width, at.Y + (BoxHeight / 2)),
            Side.Left => new Point(at.X, at.Y + (BoxHeight / 2)),
            _ => new Point(at.X + (width / 2), at.Y),
        };
        Point native = anchors[target];
        var to = new Point(PadLeft + (native.X * PadScale), PadTop + (native.Y * PadScale));
        var line = new Polyline { Stroke = _line, StrokeThickness = 1 };
        line.Points = anchor == Side.Top
            ? new PointCollection { from, new Point(from.X, from.Y - 16), to }
            : new PointCollection { from, new Point(from.X + (anchor == Side.Right ? 24 : -24), from.Y), to };
        var dot = new Ellipse { Width = 6, Height = 6, Fill = _line };
        Canvas.SetLeft(dot, to.X - 3);
        Canvas.SetTop(dot, to.Y - 3);
        lines.Children.Add(line);
        lines.Children.Add(dot);

        var callout = new Callout(row, button, title, key, line, dot);
        callout.OnRowChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(BindingRow.DisplayKey) or nameof(BindingRow.IsCapturing) or nameof(BindingRow.Label))
                Paint(callout);
        };
        row.PropertyChanged += callout.OnRowChanged;
        _callouts.Add(callout);
        Paint(callout);
    }

    private void Paint(Callout callout)
    {
        BindingRow row = callout.Row;
        bool hot = callout.Live || row.IsCapturing;
        callout.Title.Text = row.Label;
        AutomationProperties.SetName(callout.Button, "Remap " + row.Label);
        callout.Key.Text = row.DisplayKey;
        callout.Key.Foreground = row.IsCapturing || row.IsBound ? _hot : _subtle;
        callout.Key.FontStyle = row.IsBound || row.IsCapturing ? Windows.UI.Text.FontStyle.Normal : Windows.UI.Text.FontStyle.Italic;
        callout.Button.BorderBrush = hot ? _hot : _line;
        callout.Button.Background = callout.Live ? Res("ForgeAccentSoftBrush") : Res("ForgeCardBrush");
        callout.Line.Stroke = hot ? _hot : _line;
        callout.Dot.Fill = hot ? _hot : _line;
    }

    private static bool IsLive(ControlTarget target, PadFrame frame) => target switch
    {
        ControlTarget.LeftTrigger => frame.LeftTrigger > 0.05f,
        ControlTarget.RightTrigger => frame.RightTrigger > 0.05f,
        ControlTarget.LeftStickUp => frame.LeftY > 0.05f,
        ControlTarget.LeftStickDown => frame.LeftY < -0.05f,
        ControlTarget.LeftStickLeft => frame.LeftX < -0.05f,
        ControlTarget.LeftStickRight => frame.LeftX > 0.05f,
        _ => ButtonFor(target) is var button && button != GamepadButtons.None && frame.Buttons.HasFlag(button),
    };

    private static GamepadButtons ButtonFor(ControlTarget target) => target switch
    {
        ControlTarget.A => GamepadButtons.A,
        ControlTarget.B => GamepadButtons.B,
        ControlTarget.X => GamepadButtons.X,
        ControlTarget.Y => GamepadButtons.Y,
        ControlTarget.LeftShoulder => GamepadButtons.LeftShoulder,
        ControlTarget.RightShoulder => GamepadButtons.RightShoulder,
        ControlTarget.Start => GamepadButtons.Start,
        ControlTarget.Back => GamepadButtons.Back,
        ControlTarget.LeftStick => GamepadButtons.LeftStick,
        ControlTarget.RightStick => GamepadButtons.RightStick,
        ControlTarget.DpadUp => GamepadButtons.DpadUp,
        ControlTarget.DpadDown => GamepadButtons.DpadDown,
        ControlTarget.DpadLeft => GamepadButtons.DpadLeft,
        ControlTarget.DpadRight => GamepadButtons.DpadRight,
        _ => GamepadButtons.None,
    };

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private enum Side
    {
        Left,
        Right,
        Top,
    }

    private sealed class Callout(BindingRow row, Button button, TextBlock title, TextBlock key, Polyline line, Ellipse dot)
    {
        public BindingRow Row { get; } = row;
        public Button Button { get; } = button;
        public TextBlock Title { get; } = title;
        public TextBlock Key { get; } = key;
        public Polyline Line { get; } = line;
        public Ellipse Dot { get; } = dot;
        public bool Live { get; set; }
        public PropertyChangedEventHandler OnRowChanged { get; set; } = (_, _) => { };
    }
}
