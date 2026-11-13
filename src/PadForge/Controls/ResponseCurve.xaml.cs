using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PadForge.Core.Mapping;
using Windows.Foundation;

namespace PadForge.Controls;

/// <summary>Plots stick input against output for the current shaping, with a live marker.</summary>
public sealed partial class ResponseCurve : UserControl
{
    private float _curve = float.NaN;
    private float _deadzone;
    private float _antiDeadzone;

    public ResponseCurve()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
    }

    public void Plot(double curve, double deadzone, double antiDeadzone)
    {
        if ((float)curve == _curve && (float)deadzone == _deadzone && (float)antiDeadzone == _antiDeadzone)
            return;

        _curve = (float)curve;
        _deadzone = (float)deadzone;
        _antiDeadzone = (float)antiDeadzone;
        Redraw();
    }

    /// <summary>Marks the raw (pre-shaping) magnitude and where it lands on the curve.</summary>
    public void Mark(float rawMagnitude)
    {
        double width = PlotCanvas.ActualWidth;
        double height = PlotCanvas.ActualHeight;
        if (width < 2 || rawMagnitude < 0.01f || float.IsNaN(_curve))
        {
            Marker.Visibility = Visibility.Collapsed;
            return;
        }

        float input = Math.Clamp(rawMagnitude, 0f, 1f);
        var (output, _) = StickShaper.Apply(input, 0f, _curve, _deadzone, _antiDeadzone);
        Canvas.SetLeft(Marker, (input * width) - 4.5);
        Canvas.SetTop(Marker, height - (Math.Clamp(output, 0f, 1f) * height) - 4.5);
        Marker.Visibility = Visibility.Visible;
    }

    private void Redraw()
    {
        double width = PlotCanvas.ActualWidth;
        double height = PlotCanvas.ActualHeight;
        if (width < 2 || height < 2 || float.IsNaN(_curve))
            return;

        const int steps = 48;
        var guide = new PointCollection();
        var trace = new PointCollection();
        for (int i = 0; i <= steps; i++)
        {
            float input = i / (float)steps;
            var (output, _) = StickShaper.Apply(input, 0f, _curve, _deadzone, _antiDeadzone);
            double x = input * width;
            guide.Add(new Point(x, height - (input * height)));
            trace.Add(new Point(x, height - (Math.Clamp(output, 0f, 1f) * height)));
        }

        Guide.Points = guide;
        Trace.Points = trace;
    }
}
