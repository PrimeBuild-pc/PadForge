using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PadForge.Core.Input;
using PadForge.Core.Mapping;
using PadForge.Core.Profiles;
using PadForge.Core.Runtime;
using PadForge.Platform;

namespace PadForge.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly (ControlTarget Target, string Label, string Group)[] Catalog =
    [
        (ControlTarget.LeftStickUp, "Up", "Left stick"),
        (ControlTarget.LeftStickDown, "Down", "Left stick"),
        (ControlTarget.LeftStickLeft, "Left", "Left stick"),
        (ControlTarget.LeftStickRight, "Right", "Left stick"),
        (ControlTarget.A, "A", "Face"),
        (ControlTarget.B, "B", "Face"),
        (ControlTarget.X, "X", "Face"),
        (ControlTarget.Y, "Y", "Face"),
        (ControlTarget.LeftShoulder, "LB", "Shoulders and triggers"),
        (ControlTarget.RightShoulder, "RB", "Shoulders and triggers"),
        (ControlTarget.LeftTrigger, "LT", "Shoulders and triggers"),
        (ControlTarget.RightTrigger, "RT", "Shoulders and triggers"),
        (ControlTarget.DpadUp, "Up", "D-pad"),
        (ControlTarget.DpadDown, "Down", "D-pad"),
        (ControlTarget.DpadLeft, "Left", "D-pad"),
        (ControlTarget.DpadRight, "Right", "D-pad"),
        (ControlTarget.Start, "Start", "Menu"),
        (ControlTarget.Back, "Back", "Menu"),
        (ControlTarget.LeftStick, "Left stick click", "Menu"),
        (ControlTarget.RightStick, "Right stick click", "Menu"),
    ];

    private readonly PadEngine _engine;
    private readonly IKeyboardSource _keys;
    private readonly IHotkeyBinding _hotkeys;
    private readonly ProfileStore _store;
    private readonly long _mouseIgnoreTicks = (long)(0.3 * Stopwatch.Frequency);

    private bool _ready;
    private BindingRow? _captureRow;
    private bool _captureHotkey;
    private long _ignoreMouseUntil;
    private string? _seenFault;
    private string? _seenHotkeyStatus;

    public MainViewModel(
        PadEngine engine,
        IKeyboardSource keys,
        IHotkeyBinding hotkeys,
        ProfileStore store,
        Profile profile)
    {
        _engine = engine;
        _keys = keys;
        _hotkeys = hotkeys;
        _store = store;

        string? previousGroup = null;
        foreach ((ControlTarget target, string label, string group) in Catalog)
        {
            Bindings.Add(new BindingRow(target, label, group, group != previousGroup, BeginCapture));
            previousGroup = group;
        }

        ReloadList(profile.Name);
        ApplyProfile(profile, announce: false);
    }

    public ObservableCollection<BindingRow> Bindings { get; } = new();

    public ObservableCollection<string> Profiles { get; } = new();

    public PadFrame Frame { get; private set; } = PadFrame.Neutral;

    public bool BusReady { get; private set; }

    public bool BusMissing { get; private set; }

    public bool Connected { get; private set; }

    public bool Sending { get; private set; }

    public event Action? CaptureStarted;

    [ObservableProperty]
    private string _busHeadline = "Checking driver";

    [ObservableProperty]
    private string _linkHeadline = "Controller off";

    [ObservableProperty]
    private string _outputHeadline = "Idle";

    [ObservableProperty]
    private string _actionLabel = "Connect";

    [ObservableProperty]
    private bool _actionEnabled;

    [ObservableProperty]
    private bool _showBusWarning;

    [ObservableProperty]
    private string _statusDetail = "Move the mouse to see the right stick. WASD moves the left stick.";

    [ObservableProperty]
    private string _profileName = "Default";

    [ObservableProperty]
    private string? _selectedProfile;

    [ObservableProperty]
    private double _sensitivity = 1;

    [ObservableProperty]
    private double _curve = 1.4;

    [ObservableProperty]
    private double _deadzone = 0.06;

    [ObservableProperty]
    private bool _spring = true;

    [ObservableProperty]
    private double _springRate = 26;

    [ObservableProperty]
    private string _hotkeyLabel = "F8";

    [ObservableProperty]
    private string _hotkeyCaptureLabel = "Change";

    [ObservableProperty]
    private bool _isCapturing;

    public int HotkeyVirtualKey { get; private set; } = VirtualKeyCodes.F8;

    public string SensitivityText => Sensitivity.ToString("0.00");

    public string CurveText => Curve.ToString("0.00");

    public string DeadzoneText => Deadzone.ToString("0.00");

    public string SpringRateText => SpringRate.ToString("0");

    public string SpringHint => Spring
        ? "The stick follows mouse speed and returns to center."
        : "The stick holds its position until you move it back.";

    public void UseForegroundPause(IntPtr windowHandle)
    {
        _engine.CanSubmit = () => !ForegroundWindow.IsForeground(windowHandle);
    }

    public void Pull()
    {
        EngineSnapshot snapshot = _engine.Snapshot;
        Frame = snapshot.Frame;
        BusReady = snapshot.BusReady;
        BusMissing = snapshot.BusKnown && !snapshot.BusReady;
        Connected = snapshot.Connected;
        Sending = snapshot.Sending;

        BusHeadline = !snapshot.BusKnown ? "Checking driver" : snapshot.BusReady ? "Driver ready" : "Driver missing";
        LinkHeadline = snapshot.Connected ? "Controller on" : "Controller off";
        OutputHeadline = !snapshot.Connected ? "Idle" : snapshot.Sending ? "Sending" : "Paused";
        ActionEnabled = snapshot.BusReady;
        ActionLabel = snapshot.Connected ? "Disconnect" : "Connect";
        ShowBusWarning = BusMissing;

        if (snapshot.Fault != _seenFault)
        {
            _seenFault = snapshot.Fault;
            if (!string.IsNullOrEmpty(snapshot.Fault))
                StatusDetail = snapshot.Fault;
        }

        string? hotkeyStatus = _hotkeys.HotkeyStatus;
        if (hotkeyStatus != _seenHotkeyStatus)
        {
            _seenHotkeyStatus = hotkeyStatus;
            if (!string.IsNullOrEmpty(hotkeyStatus) && !IsCapturing)
                StatusDetail = hotkeyStatus;
        }

        PollCaptureMouse();
    }

    public bool TryCaptureKey(int virtualKey)
    {
        if (_captureHotkey)
        {
            if (virtualKey == 0 || VirtualKeyCodes.IsMouseButton(virtualKey))
            {
                StatusDetail = "Choose a keyboard key for the toggle.";
                return true;
            }

            HotkeyVirtualKey = virtualKey;
            HotkeyLabel = KeyNames.Describe(virtualKey);
            _captureHotkey = false;
            HotkeyCaptureLabel = "Change";
            FinishCapture();
            PushSettings(resetStick: false);
            _hotkeys.SetHotkey(virtualKey);
            StatusDetail = "Toggle hotkey updated. Save the profile to keep it.";
            return true;
        }

        if (_captureRow is null)
            return false;

        _captureRow.SetKey(virtualKey);
        _captureRow.IsCapturing = false;
        _captureRow = null;
        FinishCapture();
        PushSettings(resetStick: false);
        StatusDetail = virtualKey == 0
            ? "Binding cleared. Save the profile to keep it."
            : "Binding updated. Save the profile to keep it.";
        return true;
    }

    public void CancelCapture()
    {
        if (_captureRow is not null)
            _captureRow.IsCapturing = false;

        _captureRow = null;
        _captureHotkey = false;
        HotkeyCaptureLabel = "Change";
        FinishCapture();
        StatusDetail = "Capture cancelled.";
    }

    [RelayCommand]
    private void ToggleOutput() => _engine.RequestToggle();

    [RelayCommand]
    private void CenterStick() => _engine.RequestCenter();

    [RelayCommand]
    private void BeginHotkeyCapture()
    {
        ClearCaptureRow();
        _captureHotkey = true;
        HotkeyCaptureLabel = "Press a key";
        _ignoreMouseUntil = Stopwatch.GetTimestamp() + _mouseIgnoreTicks;
        RaiseCapture();
        StatusDetail = "Press a keyboard key. Escape cancels.";
    }

    [RelayCommand]
    private void SaveProfile()
    {
        Profile profile = CaptureProfile();
        try
        {
            _store.Save(profile);
            ProfileName = profile.Name;
            ReloadList(profile.Name);
            StatusDetail = "Profile saved.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusDetail = "Could not save the profile.";
        }
    }

    [RelayCommand]
    private void ReloadProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfile))
            return;

        try
        {
            ApplyProfile(_store.Load(SelectedProfile), announce: false);
            StatusDetail = "Profile reloaded.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusDetail = "Could not read that profile.";
        }
    }

    partial void OnSelectedProfileChanged(string? value)
    {
        if (!_ready || string.IsNullOrWhiteSpace(value))
            return;

        try
        {
            ApplyProfile(_store.Load(value), announce: false);
            StatusDetail = "Loaded " + value + ".";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusDetail = "Could not read that profile.";
        }
    }

    partial void OnSensitivityChanged(double value)
    {
        OnPropertyChanged(nameof(SensitivityText));
        if (_ready)
            PushSettings(resetStick: false);
    }

    partial void OnCurveChanged(double value)
    {
        OnPropertyChanged(nameof(CurveText));
        if (_ready)
            PushSettings(resetStick: false);
    }

    partial void OnDeadzoneChanged(double value)
    {
        OnPropertyChanged(nameof(DeadzoneText));
        if (_ready)
            PushSettings(resetStick: false);
    }

    partial void OnSpringChanged(bool value)
    {
        OnPropertyChanged(nameof(SpringHint));
        if (_ready)
            PushSettings(resetStick: true);
    }

    partial void OnSpringRateChanged(double value)
    {
        OnPropertyChanged(nameof(SpringRateText));
        if (_ready)
            PushSettings(resetStick: false);
    }

    private void BeginCapture(BindingRow row)
    {
        ClearCaptureRow();
        _captureHotkey = false;
        HotkeyCaptureLabel = "Change";
        _captureRow = row;
        row.IsCapturing = true;
        _ignoreMouseUntil = Stopwatch.GetTimestamp() + _mouseIgnoreTicks;
        RaiseCapture();
        StatusDetail = "Press a key or mouse button. Backspace clears. Escape cancels.";
    }

    private void PollCaptureMouse()
    {
        if (_captureRow is null || Stopwatch.GetTimestamp() < _ignoreMouseUntil)
            return;

        ReadOnlySpan<int> buttons =
        [
            VirtualKeyCodes.MouseLeft,
            VirtualKeyCodes.MouseRight,
            VirtualKeyCodes.MouseMiddle,
            VirtualKeyCodes.MouseX1,
            VirtualKeyCodes.MouseX2,
        ];

        foreach (int virtualKey in buttons)
        {
            if (_keys.IsDown(virtualKey))
            {
                TryCaptureKey(virtualKey);
                return;
            }
        }
    }

    private void ApplyProfile(Profile profile, bool announce)
    {
        _ready = false;
        ProfileName = profile.Name;
        HotkeyVirtualKey = profile.HotkeyVirtualKey == 0 ? VirtualKeyCodes.F8 : profile.HotkeyVirtualKey;
        HotkeyLabel = KeyNames.Describe(HotkeyVirtualKey);
        Sensitivity = Math.Clamp(profile.RightStick.Sensitivity, 0.2, 4);
        Curve = Math.Clamp(profile.RightStick.Curve, 0.6, 3);
        Deadzone = Math.Clamp(profile.RightStick.Deadzone, 0, 0.3);
        Spring = profile.RightStick.Spring;
        SpringRate = Math.Clamp(profile.RightStick.SpringRate, 4, 40);
        foreach (BindingRow row in Bindings)
            row.SetKey(profile.Binding(row.Target));

        _ready = true;
        PushSettings(resetStick: true);
        _hotkeys.SetHotkey(HotkeyVirtualKey);
        if (announce)
            StatusDetail = "Loaded " + profile.Name + ".";
    }

    private Profile CaptureProfile()
    {
        var profile = new Profile
        {
            Name = ProfileName,
            HotkeyVirtualKey = HotkeyVirtualKey,
            RightStick = new StickSettings
            {
                Sensitivity = Sensitivity,
                Curve = Curve,
                Deadzone = Deadzone,
                Spring = Spring,
                SpringRate = SpringRate,
            },
        };

        foreach (BindingRow row in Bindings)
            profile.SetBinding(row.Target, row.VirtualKey);

        return profile;
    }

    private void PushSettings(bool resetStick)
    {
        _engine.Apply(CaptureProfile().ToActive(), resetStick);
    }

    private void ReloadList(string select)
    {
        bool wasReady = _ready;
        _ready = false;
        Profiles.Clear();
        foreach (string name in _store.List())
            Profiles.Add(name);

        if (!Profiles.Contains(select))
            Profiles.Add(select);

        SelectedProfile = select;
        _ready = wasReady;
    }

    private void ClearCaptureRow()
    {
        if (_captureRow is not null)
            _captureRow.IsCapturing = false;

        _captureRow = null;
    }

    private void FinishCapture()
    {
        IsCapturing = false;
    }

    private void RaiseCapture()
    {
        IsCapturing = true;
        CaptureStarted?.Invoke();
    }
}
