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
        profile.SetBinding(ControlTarget.A, VirtualKeyCodes.Space);

        store.Save(profile);
        Profile loaded = store.Load("Arena 1");

        Assert.Equal(1.75, loaded.RightStick.Sensitivity);
        Assert.False(loaded.RightStick.Spring);
        Assert.Equal(VirtualKeyCodes.Space, loaded.Binding(ControlTarget.A));
        Assert.Equal(VirtualKeyCodes.W, loaded.Binding(ControlTarget.LeftStickUp));
        Assert.Contains("Arena 1", store.List());
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

        public void Connect() => IsConnected = true;

        public void Disconnect() => IsConnected = false;

        public void Submit(in GamepadReport report) => Reports.Add(report);

        public void Dispose()
        {
        }
    }
}
