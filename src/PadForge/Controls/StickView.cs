using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace PadForge.Controls;

/// <summary>
/// A round stick gauge: crosshair, the deadzone ring, a line from center and
/// a dot at the current deflection. Sticks are -1..1 with +Y up.
/// </summary>
public sealed class StickView : Grid
{
    private readonly Ellipse _deadzone = new() { StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
    private readonly Line _arm = new() { StrokeThickness = 2 };
    private readonly Ellipse _dot = new() { Width = 12, Height = 12 };
    private readonly TranslateTransform _shift = new();
    private double _lastDeadzone = -1;

    public StickView()
    {
        Width = 150;
        Height = 150;
        Brush line = Res("ForgeCardBorderBrush");
        Brush accent = Res("ForgeAccentBrush");

        Children.Add(new Ellipse { Fill = Res("ForgeWellBrush"), Stroke = line, StrokeThickness = 1.5 });
        Children.Add(new Rectangle { Width = 1, Fill = line, Margin = new Thickness(0, 8, 0, 8) });
        Children.Add(new Rectangle { Height = 1, Fill = line, Margin = new Thickness(8, 0, 8, 0) });
        _deadzone.Stroke = Res("ForgeSubtleBrush");
        _deadzone.HorizontalAlignment = HorizontalAlignment.Center;
        _deadzone.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_deadzone);

        var canvas = new Canvas();
        _arm.Stroke = Res("ForgeAccentLineBrush");
        canvas.Children.Add(_arm);
        Children.Add(canvas);

        _dot.Fill = accent;
        _dot.HorizontalAlignment = HorizontalAlignment.Center;
        _dot.VerticalAlignment = VerticalAlignment.Center;
        _dot.RenderTransform = _shift;
        Children.Add(_dot);
    }

    public void Apply(float x, float y, double deadzone)
    {
        double radius = (Width / 2) - 6;
        if (Math.Abs(deadzone - _lastDeadzone) > 0.0001)
        {
            _lastDeadzone = deadzone;
            double size = Math.Max(0, deadzone * radius * 2);
            _deadzone.Width = size;
            _deadzone.Height = size;
            _deadzone.Visibility = size > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        double dx = Math.Clamp(x, -1f, 1f) * radius;
        double dy = -Math.Clamp(y, -1f, 1f) * radius;
        _shift.X = dx;
        _shift.Y = dy;
        double cx = Width / 2;
        double cy = Height / 2;
        _arm.X1 = cx;
        _arm.Y1 = cy;
        _arm.X2 = cx + dx;
        _arm.Y2 = cy + dy;
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
