using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PadForge.Core.Output;
using PadForge.ViewModels;

namespace PadForge.Views;

public sealed partial class ControllerPage : UserControl, IFramePage
{
    public ControllerPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    public void Render(MainViewModel viewModel)
    {
        bool ds4 = viewModel.PadKind == PadKind.DualShock4;
        XboxChip.Visibility = ds4 ? Visibility.Collapsed : Visibility.Visible;
        Ds4Chip.Visibility = ds4 ? Visibility.Visible : Visibility.Collapsed;
        XboxCard.BorderBrush = Res(ds4 ? "ForgeCardBorderBrush" : "ForgeAccentBrush");
        Ds4Card.BorderBrush = Res(ds4 ? "ForgeAccentBrush" : "ForgeCardBorderBrush");
        BusDot.Fill = Res(viewModel.BusReady ? "ForgeGoodBrush" : viewModel.BusMissing ? "ForgeBadBrush" : "ForgeSubtleBrush");
    }

    private void Xbox_Click(object sender, RoutedEventArgs e) => ViewModel.PadKind = PadKind.Xbox360;

    private void Ds4_Click(object sender, RoutedEventArgs e) => ViewModel.PadKind = PadKind.DualShock4;

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
