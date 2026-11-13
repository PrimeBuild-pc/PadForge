using Microsoft.UI.Xaml.Controls;
using PadForge.ViewModels;

namespace PadForge.Views;

public sealed partial class SettingsPage : UserControl
{
    public SettingsPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }
}
