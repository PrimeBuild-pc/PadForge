using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PadForge.Core.Mapping;
using PadForge.ViewModels;

namespace PadForge.Views;

/// <summary>A page that redraws live input on every UI frame while it is visible.</summary>
public interface IFramePage
{
    void Render(MainViewModel viewModel);
}

public sealed partial class OverviewPage : UserControl, IFramePage
{
    public OverviewPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    public void Render(MainViewModel viewModel)
    {
        PadFrame frame = viewModel.Frame;
        Pad.SetKind(viewModel.PadKind);
        Pad.Apply(frame);
        LeftGauge.Apply(frame.LeftX, frame.LeftY, 0);
        RightGauge.Apply(frame.RightX, frame.RightY, viewModel.Deadzone);
        LtFill.Width = frame.LeftTrigger * 150;
        RtFill.Width = frame.RightTrigger * 150;
        Readout.Text = $"L {frame.LeftX:+0.00;-0.00; 0.00} {frame.LeftY:+0.00;-0.00; 0.00}   R {frame.RightX:+0.00;-0.00; 0.00} {frame.RightY:+0.00;-0.00; 0.00}";
        HeroDot.Fill = (Brush)Application.Current.Resources[
            viewModel.Sending ? "ForgeGoodBrush" : viewModel.Connected ? "ForgeWarnBrush" : viewModel.BusMissing ? "ForgeBadBrush" : "ForgeSubtleBrush"];
    }
}
