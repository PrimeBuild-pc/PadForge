using PadForge.Core.Input;
using PadForge.Core.Mapping;
using PadForge.Core.Output;
using PadForge.Core.Profiles;
using PadForge.Core.Runtime;

namespace PadForge.Core.Tests;

public class ProfileAndEngineTests
{
    [Fact]
    public void Profile_RoundTripsBindingsAndStickSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), "PadForgeTests", Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(root);
        var profile = Profile.CreateDefault();
        profile.Name = "Arena 1";
        profile.RightStick.Sensitivity = 1.75;
        profile.RightStick.Spring = false;
        profile.RightStick.AntiDeadzone = 0.15;
        profile.RightStick.InvertY = true;
        profile.RightStick.VerticalScale = 1.25;
        profile.SetBinding(ControlTarget.A, VirtualKeyCodes.Space);

        store.Save(profile);
        Profile loaded = store.Load("Arena 1");

        Assert.Equal(1.75, loaded.RightStick.Sensitivity);
        Assert.False(loaded.RightStick.Spring);
        Assert.Equal(0.15, loaded.RightStick.AntiDeadzone);
        Assert.True(loaded.RightStick.InvertY);
        Assert.Equal(1.25, loaded.RightStick.VerticalScale);
        Assert.Equal(0.15f, loaded.ToActive().RightStick.AntiDeadzone);
        Assert.True(loaded.ToActive().RightStick.InvertY);
        Assert.Equal(VirtualKeyCodes.Space, loaded.Binding(ControlTarget.A));
        Assert.Equal(VirtualKeyCodes.W, loaded.Binding(ControlTarget.LeftStickUp));
        Assert.Contains("Arena 1", store.List());
    }

    [Fact]
    public void Profile_MissingStickFields_KeepPreviousBehavior()
    {
        string root = Path.Combine(Path.GetTempPath(), "PadForgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "Old.json"),
            """
            {
              "version": 1,
              "name": "Old",
              "hotkeyVirtualKey": 119,
              "rightStick": {
                "sensitivity": 1.2,
                "curve": 1.4,
                "deadzone": 0.06,
                "spring": true,
                "springRate": 26
              },
              "bindings": {}
            }
            """);

        Profile loaded = new ProfileStore(root).Load("Old");

        Assert.Equal(VirtualKeyCodes.LeftControl, loaded.Binding(ControlTarget.LeftStickWalk));
        Assert.True(loaded.HideInput);
        Assert.Equal(Profile.CurrentVersion, loaded.Version);
        Assert.Equal(0, loaded.RightStick.AntiDeadzone);
        Assert.False(loaded.RightStick.InvertX);
        Assert.False(loaded.RightStick.InvertY);
        Assert.Equal(1, loaded.RightStick.VerticalScale);
        StickTuning active = loaded.ToActive().RightStick;
        Assert.Equal(0f, active.AntiDeadzone);
        Assert.Equal(1f, active.VerticalScale);
    }

    [Fact]
    public void Engine_CalibrationRecordsPeakThenFinishes()
    {
        var mouse = new ScriptedMouse();
        var engine = new PadEngine(mouse, new ScriptedKeys(), new RecordingPad(), Profile.CreateDefault().ToActive());
        engine.RequestCalibration(0.25f);
        mouse.NextX = 1600;

        engine.Tick(0.02f);

        Assert.True(engine.Snapshot.Calibrating);
        Assert.Equal(1600f / 0.02f, engine.Snapshot.CalibrationPeak, 0);

        for (int i = 0; i < 11; i++)
            engine.Tick(0.02f);

        Assert.True(engine.Snapshot.Calibrating);
        engine.Tick(0.02f);

        Assert.True(engine.Snapshot.CalibrationReady);
        Assert.False(engine.Snapshot.Calibrating);
        Assert.True(FlickCalibration.TrySensitivity(engine.Snapshot.CalibrationPeak, out float sensitivity));
        Assert.Equal(4f, sensitivity);

        engine.RequestClearCalibration();
        engine.Tick(0.02f);
        Assert.False(engine.Snapshot.CalibrationReady);
        engine.Dispose();
    }

    [Fact]
    public void Engine_CancelCalibrationDropsTheSample()
    {
        var mouse = new ScriptedMouse();
        var engine = new PadEngine(mouse, new ScriptedKeys(), new RecordingPad(), Profile.CreateDefault().ToActive());
        engine.RequestCalibration(1f);
        engine.RequestCancelCalibration();
        mouse.NextX = 2000;

        engine.Tick(0.02f);

        Assert.False(engine.Snapshot.Calibrating);
        Assert.False(engine.Snapshot.CalibrationReady);
        Assert.Equal(0f, engine.Snapshot.CalibrationPeak);
        engine.Dispose();
    }

    [Theory]
    [InlineData("../../../windows", "windows")]
    [InlineData("   ", "Profile")]
    [InlineData("Night Ops", "Night Ops")]
    public void SanitizeName_StripsPathCharacters(string raw, string expected)
    {
        Assert.Equal(expected, ProfileStore.SanitizeName(raw));
    }

    [Fact]
    public void Engine_ConnectsAndSubmitsMappedInput()
    {
        var mouse = new ScriptedMouse();
        var keys = new ScriptedKeys();
        var pad = new RecordingPad();
        var engine = new PadEngine(mouse, keys, pad, Profile.CreateDefault().ToActive());
        engine.PublishBus(true);
        engine.RequestConnect();
        keys.Down.Add(VirtualKeyCodes.Space);
        mouse.NextX = 400;

        engine.Tick(0.02f);

        Assert.True(pad.IsConnected);
        Assert.NotEmpty(pad.Reports);
        Assert.True(pad.Reports[^1].Buttons.HasFlag(GamepadButtons.A));
        Assert.True(pad.Reports[^1].RightX > 1000);
        Assert.True(engine.Snapshot.Sending);

        engine.CanSubmit = () => false;
        engine.Tick(0.02f);

        Assert.Equal(0, pad.Reports[^1].RightX);
        Assert.Equal(GamepadButtons.None, pad.Reports[^1].Buttons);
        Assert.False(engine.Snapshot.Sending);

        engine.RequestDisconnect();
        engine.Tick(0.02f);
        Assert.False(pad.IsConnected);
        engine.Dispose();
    }

    [Fact]
    public void Engine_RefusesToConnectWithoutViGEmBus()
    {
        var pad = new RecordingPad();
        var engine = new PadEngine(new ScriptedMouse(), new ScriptedKeys(), pad, Profile.CreateDefault().ToActive());
        engine.PublishBus(false);
        engine.RequestConnect();

        engine.Tick(0.02f);

        Assert.False(pad.IsConnected);
        Assert.Equal("ViGEmBus is not installed.", engine.Snapshot.Fault);
        engine.Dispose();
    }

    [Fact]
    public void Store_SkipsABrokenProfileAndPrefersTheLastUsedOne()
    {
        string root = Path.Combine(Path.GetTempPath(), "PadForgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Default.json"), "{ not json");
        var store = new ProfileStore(root);

        Profile fallback = store.LoadOrCreateDefault();
        Assert.Equal("Default", fallback.Name);

        var arena = Profile.CreateDefault();
        arena.Name = "Arena";
        arena.WalkScale = 0.3;
        arena.HideInput = false;
        store.Save(arena);
        store.LastUsed = "Arena";

        Profile reopened = store.LoadOrCreateDefault();
        Assert.Equal("Arena", reopened.Name);
        Assert.Equal(0.3, reopened.WalkScale);
        Assert.False(reopened.HideInput);

        store.Delete("Arena");
        Assert.False(store.Exists("Arena"));
        Assert.DoesNotContain("Arena", store.List());
    }

    [Fact]
    public void Engine_HidesInputOnlyWhileSending()
    {
        var keys = new ScriptedKeys();
        var blocker = new RecordingBlocker();
        var engine = new PadEngine(new ScriptedMouse(), keys, new RecordingPad(), Profile.CreateDefault().ToActive(), blocker);
        engine.PublishBus(true);

        engine.Tick(0.02f);
        Assert.False(blocker.Active);

        engine.RequestConnect();
        engine.Tick(0.02f);
        Assert.True(blocker.Active);
        Assert.Equal(VirtualKeyCodes.F8, blocker.PassThrough);
        Assert.Contains(VirtualKeyCodes.W, blocker.Keys);

        engine.CanSubmit = () => false;
        engine.Tick(0.02f);
        Assert.False(blocker.Active);

        engine.CanSubmit = null;
        var open = Profile.CreateDefault();
        open.HideInput = false;
        engine.Apply(open.ToActive(), resetStick: false);
        engine.Tick(0.02f);
        Assert.False(blocker.Active);

        engine.Dispose();
    }

    [Fact]
    public void Engine_WalkKeyScalesTheLeftStick()
    {
        var keys = new ScriptedKeys();
        var engine = new PadEngine(new ScriptedMouse(), keys, new RecordingPad(), Profile.CreateDefault().ToActive());
        keys.Down.Add(VirtualKeyCodes.W);
        engine.Tick(0.02f);
        Assert.Equal(1f, engine.Snapshot.Frame.LeftY, 3);

        keys.Down.Add(VirtualKeyCodes.LeftControl);
        engine.Tick(0.02f);
        Assert.Equal(0.5f, engine.Snapshot.Frame.LeftY, 3);
        engine.Dispose();
    }

    [Fact]
    public void Engine_SwitchingControllerTypeRecreatesThePad()
    {
        var pad = new RecordingPad();
        var engine = new PadEngine(new ScriptedMouse(), new ScriptedKeys(), pad, Profile.CreateDefault().ToActive());
        engine.PublishBus(true);
        engine.RequestConnect();
        engine.Tick(0.02f);
        Assert.Equal(PadKind.Xbox360, pad.Kind);

        var ds4 = Profile.CreateDefault();
        ds4.PadKind = PadKind.DualShock4;
        engine.Apply(ds4.ToActive(), resetStick: false);
        engine.Tick(0.02f);

        Assert.True(pad.IsConnected);
        Assert.Equal(PadKind.DualShock4, pad.Kind);
        Assert.Equal(2, pad.Connects);
        Assert.Equal(PadKind.DualShock4, engine.Snapshot.PadKind);

        engine.Apply(ds4.ToActive(), resetStick: false);
        engine.Tick(0.02f);
        Assert.Equal(2, pad.Connects);
        engine.Dispose();
    }

    private sealed class RecordingBlocker : IInputBlocker
    {
        public bool Active { get; private set; }
        public int[] Keys { get; private set; } = Array.Empty<int>();
        public int PassThrough { get; private set; }

        public void SetBlocking(bool active, int[] keys, int passThroughKey)
        {
            Active = active;
            Keys = keys;
            PassThrough = passThroughKey;
        }
    }

    private sealed class ScriptedMouse : IMouseSource
    {
        public int NextX { get; set; }
        public int NextY { get; set; }

        public (int X, int Y) ConsumeDelta()
        {
            var value = (NextX, NextY);
            NextX = 0;
            NextY = 0;
            return value;
        }
    }

    private sealed class ScriptedKeys : IKeyboardSource
    {
        public HashSet<int> Down { get; } = new();

        public bool IsDown(int virtualKey) => Down.Contains(virtualKey);
    }

    private sealed class RecordingPad : IVirtualGamepad
    {
        public List<GamepadReport> Reports { get; } = new();
        public bool IsConnected { get; private set; }

        public PadKind Kind { get; set; }

        public int Connects { get; private set; }

        public void Connect()
        {
            IsConnected = true;
            Connects++;
        }

        public void Disconnect() => IsConnected = false;

        public void Submit(in GamepadReport report) => Reports.Add(report);

        public void Dispose()
        {
        }
    }
}
