using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using PadForge.Core.Mapping;
using PadForge.Platform;

namespace PadForge.ViewModels;

public partial class BindingRow : ObservableObject
{
    private readonly Action<BindingRow> _capture;

    public BindingRow(ControlTarget target, string label, string group, bool showGroup, Action<BindingRow> capture)
    {
        Target = target;
        Label = label;
        Group = group;
        ShowGroup = showGroup;
        GroupVisibility = showGroup ? Visibility.Visible : Visibility.Collapsed;
        _capture = capture;
        CaptureCommand = new RelayCommand(() => _capture(this));
    }

    public ControlTarget Target { get; }

    public string Label { get; }

    public string Group { get; }

    public bool ShowGroup { get; }

    public Visibility GroupVisibility { get; }

    public int VirtualKey { get; private set; }

    public IRelayCommand CaptureCommand { get; }

    [ObservableProperty]
    private string _keyLabel = "Unbound";

    [ObservableProperty]
    private bool _isCapturing;

    public string CaptureLabel => IsCapturing ? "Press a key" : "Change";

    public void SetKey(int virtualKey)
    {
        VirtualKey = virtualKey;
        KeyLabel = KeyNames.Describe(virtualKey);
    }

    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(CaptureLabel));
}
