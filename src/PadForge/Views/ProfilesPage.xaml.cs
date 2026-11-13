using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PadForge.ViewModels;

namespace PadForge.Views;

public sealed partial class ProfilesPage : UserControl, IFramePage
{
    public ProfilesPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    public void Render(MainViewModel viewModel)
    {
        DirtyChip.Visibility = viewModel.IsDirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        string? name = ViewModel.SelectedProfile;
        if (string.IsNullOrWhiteSpace(name))
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete " + name + "?",
            Content = "The profile file is removed from disk. This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.DeleteSelectedProfile();
    }
}
