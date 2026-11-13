using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PadForge.Core.Input;
using PadForge.Core.Mapping;
using PadForge.Core.Output;
using PadForge.Core.Profiles;
using PadForge.Core.Runtime;
using PadForge.Platform;

namespace PadForge.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // Name on an Xbox pad, then on a DualShock 4.
    private static readonly (ControlTarget Target, string Xbox, string PlayStation)[] Catalog =
    [
        (ControlTarget.LeftStickUp, "Move forward", "Move forward"),
        (ControlTarget.LeftStickDown, "Move back", "Move back"),
        (ControlTarget.LeftStickLeft, "Move left", "Move left"),
        (ControlTarget.LeftStickRight, "Move right", "Move right"),
        (ControlTarget.LeftStickWalk, "Walk (hold)", "Walk (hold)"),
        (ControlTarget.LeftStick, "L3 · stick click", "L3 · stick click"),
        (ControlTarget.A, "A", "Cross ✕"),
        (ControlTarget.B, "B", "Circle ○"),
        (ControlTarget.X, "X", "Square □"),
        (ControlTarget.Y, "Y", "Triangle △"),
        (ControlTarget.LeftShoulder, "LB · left bumper", "L1"),
        (ControlTarget.RightShoulder, "RB · right bumper", "R1"),
        (ControlTarget.LeftTrigger, "LT · left trigger", "L2"),
        (ControlTarget.RightTrigger, "RT · right trigger", "R2"),
        (ControlTarget.DpadUp, "D-pad up", "D-pad up"),
        (ControlTarget.DpadDown, "D-pad down", "D-pad down"),
        (ControlTarget.DpadLeft, "D-pad left", "D-pad left"),
        (ControlTarget.DpadRight, "D-pad right", "D-pad right"),
        (ControlTarget.Start, "Start · menu", "Options"),
        (ControlTarget.Back, "Back · view", "Share"),
        (ControlTarget.RightStick, "R3 · stick click", "R3 · stick click"),
    ];

    /// <summary>Named starting points for the right stick. Each one is a full stick setup.</summary>
    public static IReadOnlyList<StickPreset> Presets { get; } =
    [
        new("Balanced", "A safe start for most shooters.", 1.4, 0.06, 0.0, true, 26),
        new("Precise", "Slow near center for small corrections.", 1.9, 0.04, 0.08, true, 20),
        new("Fast flicks", "Quick turns, little curve.", 1.1, 0.05, 0.15, true, 34),
        new("Linear", "Straight response with a lifted start.", 1.0, 0.0, 0.22, true, 26),
        new("Hold aim", "The stick stays where the mouse leaves it.", 1.2, 0.04, 0.1, false, 26),
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
    private bool _calibrationArmed;
    private bool _calibrationHandled;

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

        foreach ((ControlTarget target, string xbox, _) in Catalog)
            Bindings.Add(new BindingRow(target, xbox, BeginCapture));

        ReloadList(profile.Name);
        ApplyProfile(profile, announce: false);
    }

    public ObservableCollection<BindingRow> Bindings { get; } = new();

    public ObservableCollection<string> Profiles { get; } = new();

    public BindingRow WalkBinding => Row(ControlTarget.LeftStickWalk);

    public PadFrame Frame { get; private set; } = PadFrame.Neutral;

    public bool BusReady { get; private set; }

    public bool BusMissing { get; private set; }

    public bool Connected { get; private set; }

    public bool Sending { get; private set; }

    public string Version { get; } =
        "Version " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");

    public string ProfilesFolder => ProfileStore.DefaultDirectory;

    public event Action? CaptureStarted;

    [ObservableProperty]
    private string _busHeadline = "Checking driver";

    [ObservableProperty]
    private string _linkHeadline = "Controller off";

    [ObservableProperty]
    private string _outputHeadline = "Idle";

    [ObservableProperty]
    private string _statusTitle = "Checking for ViGEmBus";

    [ObservableProperty]
    private string _statusBody = "PadForge needs the ViGEmBus driver to create a virtual Xbox 360 controller.";

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
    private double _antiDeadzone;

    [ObservableProperty]
    private bool _invertX;

    [ObservableProperty]
    private bool _invertY;

    [ObservableProperty]
    private double _verticalScale = 1;

    [ObservableProperty]
    private double _walkScale = 0.5;

    [ObservableProperty]
    private bool _hideInput = true;

    [ObservableProperty]
    private PadKind _padKind;

    [ObservableProperty]
    private string _calibrateLabel = "Calibrate";

    [ObservableProperty]
    private double _calibrationProgress;

    [ObservableProperty]
    private string _hotkeyLabel = "F8";

    [ObservableProperty]
    private string _hotkeyCaptureLabel = "Change";

    [ObservableProperty]
    private bool _isCapturing;

    [ObservableProperty]
    private bool _isDirty;

    public int HotkeyVirtualKey { get; private set; } = VirtualKeyCodes.F8;

    public string PadKindName => PadKind == PadKind.DualShock4 ? "DualShock 4" : "Xbox 360";

    public string SensitivityText => "×" + Sensitivity.ToString("0.00");

    public string CurveText => Curve switch
    {
        < 0.95 => "Snappy " + Curve.ToString("0.00"),
        < 1.25 => "Linear " + Curve.ToString("0.00"),
        < 1.7 => "Steady " + Curve.ToString("0.00"),
        _ => "Precise " + Curve.ToString("0.00"),
    };

    public string DeadzoneText => Deadzone.ToString("0%");

    public string SpringRateText => SpringRate.ToString("0");

    public string AntiDeadzoneText => AntiDeadzone.ToString("0%");

    public string VerticalScaleText => "×" + VerticalScale.ToString("0.00");

    public string WalkScaleText => WalkScale.ToString("0%");

    public string SpringHint => Spring
        ? "The stick follows mouse speed and returns to center when the mouse stops."
        : "The stick holds its position until you move it back. Center stick clears it.";

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

        BusHeadline = !snapshot.BusKnown ? "Checking driver" : snapshot.BusReady ? "ViGEmBus ready" : "ViGEmBus missing";
        LinkHeadline = snapshot.Connected ? "Controller on" : "Controller off";
        OutputHeadline = !snapshot.Connected ? "Idle" : snapshot.Sending ? "Sending" : "Paused";
        ActionEnabled = snapshot.BusReady;
        ActionLabel = snapshot.Connected ? "Disconnect" : "Connect controller";
        ShowBusWarning = BusMissing;
        (StatusTitle, StatusBody) = (snapshot.BusKnown, snapshot.BusReady, snapshot.Connected, snapshot.Sending) switch
        {
            (false, _, _, _) => ("Checking for ViGEmBus", "PadForge needs the ViGEmBus driver to create a virtual Xbox 360 controller."),
            (true, false, _, _) => ("ViGEmBus is not installed", "Install the driver, restart if the installer asks, then reopen PadForge."),
            (_, _, false, _) => ("Ready to connect", $"Connect, then switch to your game. {HotkeyLabel} toggles the controller from anywhere."),
            (_, _, true, true) => ("Playing", $"Your keyboard and mouse drive the virtual pad. Press {HotkeyLabel} to stop."),
            _ => ("Connected · paused", "Output pauses while PadForge is in front so you can tune safely. Switch to your game to play."),
        };

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

        TrackCalibration(snapshot);
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
            Changed();
            _hotkeys.SetHotkey(virtualKey);
            StatusDetail = "Toggle hotkey updated. Save the profile to keep it.";
            return true;
        }

        if (_captureRow is null)
            return false;

        if (virtualKey == HotkeyVirtualKey)
        {
            StatusDetail = KeyNames.Describe(virtualKey) + " toggles the controller. Pick another key.";
            return true;
        }

        _captureRow.SetKey(virtualKey);
        _captureRow.IsCapturing = false;
        _captureRow = null;
        FinishCapture();
        Changed();
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

    public BindingRow Row(ControlTarget target) => Bindings.First(row => row.Target == target);

    /// <summary>Deletes the selected profile and opens another one. The caller confirms first.</summary>
    public void DeleteSelectedProfile()
    {
        string? name = SelectedProfile;
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            _store.Delete(name);
            Profile next = _store.LoadOrCreateDefault();
            ReloadList(next.Name);
            ApplyProfile(next, announce: false);
            StatusDetail = "Deleted " + name + ".";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusDetail = "Could not delete that profile.";
        }
    }

    [RelayCommand]
    private void ToggleOutput() => _engine.RequestToggle();

    [RelayCommand]
    private void CenterStick() => _engine.RequestCenter();

    [RelayCommand]
    private void ApplyPreset(string name)
    {
        StickPreset? preset = Presets.FirstOrDefault(item => item.Name == name);
        if (preset is null)
            return;

        _ready = false;
        Curve = preset.Curve;
        Deadzone = preset.Deadzone;
        AntiDeadzone = preset.AntiDeadzone;
        Spring = preset.Spring;
        SpringRate = preset.SpringRate;
        _ready = true;
        Changed(resetStick: true);
        StatusDetail = preset.Name + " applied. Sensitivity and vertical ratio are yours to tune.";
    }

    [RelayCommand]
    private void ToggleCalibration()
    {
        if (_calibrationArmed)
        {
            _calibrationArmed = false;
            _calibrationHandled = false;
            CalibrateLabel = "Calibrate";
            CalibrationProgress = 0;
            _engine.RequestCancelCalibration();
            StatusDetail = "Calibration cancelled.";
            return;
        }

        _calibrationArmed = true;
        _calibrationHandled = false;
        CalibrateLabel = "Cancel";
        CalibrationProgress = 0;
        _engine.RequestCalibration(3f);
        StatusDetail = "Flick the mouse at the speed that should reach the edge of the stick. Three seconds.";
    }

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
            _store.LastUsed = profile.Name;
            ProfileName = profile.Name;
            ReloadList(profile.Name);
            IsDirty = false;
            StatusDetail = "Saved " + profile.Name + ".";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusDetail = "Could not save the profile.";
        }
    }

    [RelayCommand]
    private void NewProfile()
    {
        string name = UniqueName("Profile");
        Profile profile = Profile.CreateDefault();
        profile.Name = name;
        try
        {
            _store.Save(profile);
            ReloadList(name);
            ApplyProfile(profile, announce: false);
            StatusDetail = "Created " + name + " with the default layout.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusDetail = "Could not create a profile.";
        }
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        ProfileName = UniqueName(ProfileName + " copy");
        SaveProfile();
    }

    [RelayCommand]
    private void ReloadProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfile))
            return;

        if (_store.TryLoad(SelectedProfile, out Profile? profile))
        {
            ApplyProfile(profile, announce: false);
            StatusDetail = "Reverted to the saved " + profile.Name + ".";
        }
        else
        {
            StatusDetail = "Could not read that profile.";
        }
    }

    [RelayCommand]
    private void ResetBindings()
    {
        Profile defaults = Profile.CreateDefault();
        foreach (BindingRow row in Bindings)
            row.SetKey(defaults.Binding(row.Target));

        Changed();
        StatusDetail = "Bindings reset to the default layout. Save the profile to keep them.";
    }

    [RelayCommand]
    private void OpenProfilesFolder()
    {
        try
        {
            Directory.CreateDirectory(ProfilesFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + ProfilesFolder + "\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusDetail = "Could not open the profiles folder.";
        }
    }

    partial void OnSelectedProfileChanged(string? value)
    {
        if (!_ready || string.IsNullOrWhiteSpace(value) || value == ProfileName)
            return;

        if (_store.TryLoad(value, out Profile? profile))
        {
            ApplyProfile(profile, announce: false);
            StatusDetail = "Loaded " + value + ".";
        }
        else
        {
            StatusDetail = "Could not read that profile.";
        }
    }

    partial void OnSensitivityChanged(double value) => Changed(nameof(SensitivityText));

    partial void OnCurveChanged(double value) => Changed(nameof(CurveText));

    partial void OnDeadzoneChanged(double value) => Changed(nameof(DeadzoneText));

    partial void OnSpringChanged(bool value) => Changed(nameof(SpringHint), resetStick: true);

    partial void OnSpringRateChanged(double value) => Changed(nameof(SpringRateText));

    partial void OnAntiDeadzoneChanged(double value) => Changed(nameof(AntiDeadzoneText));

    partial void OnInvertXChanged(bool value) => Changed(resetStick: true);

    partial void OnInvertYChanged(bool value) => Changed(resetStick: true);

    partial void OnVerticalScaleChanged(double value) => Changed(nameof(VerticalScaleText));

    partial void OnWalkScaleChanged(double value) => Changed(nameof(WalkScaleText));

    partial void OnHideInputChanged(bool value) => Changed();

    partial void OnPadKindChanged(PadKind value)
    {
        foreach ((ControlTarget target, string xbox, string playStation) in Catalog)
            Row(target).Label = value == PadKind.DualShock4 ? playStation : xbox;

        Changed(nameof(PadKindName));
        if (_ready)
            StatusDetail = PadKindName + " selected. A connected pad is recreated as the new type.";
    }

    private void Changed(string? textProperty = null, bool resetStick = false)
    {
        if (textProperty is not null)
            OnPropertyChanged(textProperty);

        if (!_ready)
            return;

        IsDirty = true;
        PushSettings(resetStick);
    }

    private void TrackCalibration(EngineSnapshot snapshot)
    {
        if (!_calibrationArmed)
            return;

        if (snapshot.Calibrating)
        {
            CalibrationProgress = 100 * (1 - (snapshot.CalibrationRemaining / 3f));
            StatusDetail = $"Flick the mouse at the speed that should reach the edge. {snapshot.CalibrationRemaining:0.0}s left.";
            return;
        }

        if (!snapshot.CalibrationReady || _calibrationHandled)
            return;

        _calibrationHandled = true;
        _calibrationArmed = false;
        CalibrateLabel = "Calibrate";
        CalibrationProgress = 0;
        if (FlickCalibration.TrySensitivity(snapshot.CalibrationPeak, out float sensitivity))
        {
            Sensitivity = sensitivity;
            StatusDetail = $"Sensitivity set to {sensitivity:0.00} from that flick. Save the profile to keep it.";
        }
        else
        {
            StatusDetail = "No mouse movement detected. Try the calibration again.";
        }

        _engine.RequestClearCalibration();
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
        StatusDetail = "Press a key, mouse button or wheel. Backspace clears. Escape cancels.";
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
            VirtualKeyCodes.WheelUp,
            VirtualKeyCodes.WheelDown,
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
        AntiDeadzone = Math.Clamp(profile.RightStick.AntiDeadzone, 0, 0.4);
        InvertX = profile.RightStick.InvertX;
        InvertY = profile.RightStick.InvertY;
        VerticalScale = Math.Clamp(profile.RightStick.VerticalScale, 0.25, 2.5);
        Spring = profile.RightStick.Spring;
        SpringRate = Math.Clamp(profile.RightStick.SpringRate, 4, 40);
        WalkScale = Math.Clamp(profile.WalkScale, 0.1, 1);
        HideInput = profile.HideInput;
        PadKind = profile.PadKind;
        foreach (BindingRow row in Bindings)
            row.SetKey(profile.Binding(row.Target));

        _ready = true;
        IsDirty = false;
        _store.LastUsed = profile.Name;
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
            WalkScale = WalkScale,
            HideInput = HideInput,
            PadKind = PadKind,
            RightStick = new StickSettings
            {
                Sensitivity = Sensitivity,
                Curve = Curve,
                Deadzone = Deadzone,
                AntiDeadzone = AntiDeadzone,
                InvertX = InvertX,
                InvertY = InvertY,
                VerticalScale = VerticalScale,
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

    private string UniqueName(string stem)
    {
        string baseName = ProfileStore.SanitizeName(stem);
        string name = baseName;
        for (int i = 2; _store.Exists(name); i++)
            name = ProfileStore.SanitizeName(baseName + " " + i);

        return name;
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

public sealed record StickPreset(
    string Name,
    string Summary,
    double Curve,
    double Deadzone,
    double AntiDeadzone,
    bool Spring,
    double SpringRate);
