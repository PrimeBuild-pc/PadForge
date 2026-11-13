using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PadForge.Core.Mapping;
using PadForge.ViewModels;

namespace PadForge.Views;

public sealed partial class AimPage : UserControl, IFramePage
{
    public AimPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    public IReadOnlyList<StickPreset> Presets => MainViewModel.Presets;

    public void Render(MainViewModel viewModel)
    {
        PadFrame frame = viewModel.Frame;
        RightGauge.Apply(frame.RightX, frame.RightY, viewModel.Deadzone);
        LeftGauge.Apply(frame.LeftX, frame.LeftY, 0);
        Curve.Plot(viewModel.Curve, viewModel.Deadzone, viewModel.AntiDeadzone);
        Curve.Mark(frame.RightRaw);
        RightReadout.Text = $"{frame.RightX:+0.00;-0.00; 0.00} {frame.RightY:+0.00;-0.00; 0.00}";
        LeftReadout.Text = $"{frame.LeftX:+0.00;-0.00; 0.00} {frame.LeftY:+0.00;-0.00; 0.00}";
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
            ViewModel.ApplyPresetCommand.Execute(name);
    }
}
