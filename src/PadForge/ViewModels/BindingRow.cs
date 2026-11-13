using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PadForge.Core.Mapping;
using PadForge.Platform;

namespace PadForge.ViewModels;

public partial class BindingRow : ObservableObject
{
    public BindingRow(ControlTarget target, string label, Action<BindingRow> capture)
    {
        Target = target;
        _label = label;
        CaptureCommand = new RelayCommand(() => capture(this));
    }

    public ControlTarget Target { get; }

    public int VirtualKey { get; private set; }

    public IRelayCommand CaptureCommand { get; }

    /// <summary>The control's name on the emulated pad, such as "A" or "Cross".</summary>
    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _keyLabel = "Not assigned";

    [ObservableProperty]
    private bool _isCapturing;

    public string CaptureLabel => IsCapturing ? "Press a key…" : "Change";

    public string DisplayKey => IsCapturing ? "Press a key…" : KeyLabel;

    public bool IsBound => VirtualKey != 0;

    public void SetKey(int virtualKey)
    {
        VirtualKey = virtualKey;
        KeyLabel = KeyNames.Describe(virtualKey);
        OnPropertyChanged(nameof(DisplayKey));
        OnPropertyChanged(nameof(IsBound));
    }

    partial void OnIsCapturingChanged(bool value)
    {
        OnPropertyChanged(nameof(CaptureLabel));
        OnPropertyChanged(nameof(DisplayKey));
    }
}
