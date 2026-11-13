using System.Diagnostics;
using System.Runtime.InteropServices;
using PadForge.Core.Input;
using PadForge.Core.Mapping;
using PadForge.Core.Profiles;
using PadForge.Core.Runtime;
using PadForge.Output;
using PadForge.Platform;

// PadForge smoke checks.
//   driver: creates the virtual pad through ViGEmBus and reads it back with XInput, like a game would.
//   hooks:  blocks an unused key, the wheel and the cursor with synthetic input only.
// Exit code 0 means every check passed.
string mode = args.FirstOrDefault() ?? "driver";
var failures = new List<string>();

void Check(bool ok, string name, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
    if (!ok)
        failures.Add(name);
}

if (mode == "driver")
    RunDriver();
else if (mode == "hooks")
    RunHooks();
else if (mode == "ds4")
    RunDualShock4();
else
    Console.WriteLine("usage: PadForge.Smoke driver|hooks|ds4");

Console.WriteLine(failures.Count == 0 ? "ALL PASSED" : $"{failures.Count} FAILED: {string.Join(", ", failures)}");
return failures.Count == 0 ? 0 : 1;

void RunDriver()
{
    BusProbeResult probe = VigemBusProbe.Check();
    Check(probe.IsInstalled, "ViGEmBus detected", probe.Message);
    if (!probe.IsInstalled)
        return;

    int before = Native.ConnectedSlots();
    var keys = new FakeKeys();
    var mouse = new FakeMouse();
    var engine = new PadEngine(mouse, keys, new VigemGamepad(), Profile.CreateDefault().ToActive());
    engine.PublishBus(true);

    engine.RequestConnect();
    engine.Tick(0.004f);
    Check(engine.Snapshot.Connected && engine.Snapshot.Fault is null, "virtual pad connects", engine.Snapshot.Fault ?? "");

    int slot = WaitForNewSlot(before);
    Check(slot >= 0, "XInput sees a new controller", "slot " + slot);
    if (slot < 0)
    {
        engine.Dispose();
        return;
    }

    // A held button sent right after the pad appears must still arrive once Windows finishes setting it up.
    keys.Down.Add(VirtualKeyCodes.Space);
    var ready = Stopwatch.StartNew();
    Native.XInputState state = default;
    bool seen = false;
    while (ready.ElapsedMilliseconds < 10000 && !seen)
    {
        engine.Tick(0.004f);
        Thread.Sleep(4);
        seen = Native.XInputGetState(slot, out state) == 0 && (state.Gamepad.Buttons & 0x1000) != 0;
    }

    Check(seen, "held button reaches a fresh pad", $"{ready.ElapsedMilliseconds} ms after it appeared");

    state = Drive(engine, slot, () => keys.Down.Clear());
    Check(state.Gamepad.Buttons == 0 && state.Gamepad.ThumbLY == 0 && state.Gamepad.RightTrigger == 0, "neutral report", Describe(state));

    state = Drive(engine, slot, () =>
    {
        keys.Down.Clear();
        keys.Down.UnionWith([VirtualKeyCodes.Space, VirtualKeyCodes.W, VirtualKeyCodes.MouseLeft, VirtualKeyCodes.Q, VirtualKeyCodes.Up]);
    });
    Check((state.Gamepad.Buttons & 0x1000) != 0, "Space -> A", Describe(state));
    Check((state.Gamepad.Buttons & 0x0100) != 0, "Q -> LB", Describe(state));
    Check((state.Gamepad.Buttons & 0x0001) != 0, "Up -> D-pad up", Describe(state));
    Check(state.Gamepad.ThumbLY == 32767, "W -> left stick full up", Describe(state));
    Check(state.Gamepad.RightTrigger == 255, "Mouse left -> RT", Describe(state));

    state = Drive(engine, slot, () =>
    {
        keys.Down.Clear();
        keys.Down.UnionWith([VirtualKeyCodes.W, VirtualKeyCodes.LeftControl]);
    });
    Check(state.Gamepad.ThumbLY is > 15000 and < 17500, "Ctrl + W -> walk at half speed", Describe(state));

    keys.Down.Clear();
    var watch = Stopwatch.StartNew();
    short peakX = 0;
    short peakY = 0;
    while (watch.ElapsedMilliseconds < 150)
    {
        mouse.Add(16, -8);
        engine.Tick(0.004f);
        Thread.Sleep(4);
        Native.XInputGetState(slot, out state);
        peakX = Math.Max(peakX, state.Gamepad.ThumbRX);
        peakY = Math.Max(peakY, state.Gamepad.ThumbRY);
    }

    Check(peakX > 8000 && peakY > 2000, "mouse right/up -> right stick right/up", $"peak RX {peakX}, RY {peakY}");

    for (int i = 0; i < 150; i++)
        engine.Tick(0.004f);
    Native.XInputGetState(slot, out state);
    Check(Math.Abs((int)state.Gamepad.ThumbRX) < 500, "right stick springs back to center", Describe(state));

    keys.Down.Add(VirtualKeyCodes.Space);
    engine.CanSubmit = () => false;
    state = Drive(engine, slot, () => { });
    Check(state.Gamepad.Buttons == 0 && !engine.Snapshot.Sending, "paused output sends a neutral pad", Describe(state));
    engine.CanSubmit = null;

    keys.Down.Clear();
    engine.RequestToggle();
    engine.Tick(0.004f);
    Thread.Sleep(300);
    Check(!engine.Snapshot.Connected && Native.XInputGetState(slot, out _) != 0, "toggle removes the controller");

    engine.RequestToggle();
    engine.Tick(0.004f);
    int again = WaitForNewSlot(Native.ConnectedSlots() & ~(1 << slot));
    Check(engine.Snapshot.Connected && again >= 0, "toggle brings it back", "slot " + again);

    engine.Start();
    lock (keys.Down)
        keys.Down.Add(VirtualKeyCodes.Space);
    Thread.Sleep(200);
    Native.XInputGetState(again, out state);
    Check((state.Gamepad.Buttons & 0x1000) != 0, "background loop drives the pad", Describe(state));
    uint packetBefore = state.PacketNumber;
    Thread.Sleep(500);
    lock (keys.Down)
        keys.Down.Clear();
    Thread.Sleep(100);
    Native.XInputGetState(again, out state);
    Check(state.Gamepad.Buttons == 0, "release reaches the pad", Describe(state));
    Console.WriteLine($"      packets in ~0.6 s: {state.PacketNumber - packetBefore}");

    engine.Dispose();
    Thread.Sleep(300);
    Check(Native.XInputGetState(again, out _) != 0, "dispose removes the controller");
}

void RunDualShock4()
{
    // Creates a DualShock 4 and asks Plug and Play whether Windows enumerated a Sony pad.
    var pad = new VigemGamepad { Kind = PadForge.Core.Output.PadKind.DualShock4 };
    pad.Connect();
    Check(pad.IsConnected, "DualShock 4 target connects");
    bool seen = false;
    var watch = Stopwatch.StartNew();
    while (!seen && watch.ElapsedMilliseconds < 8000)
    {
        pad.Submit(new GamepadReport(0, 0, 0, 0, 0, 0, GamepadButtons.A));
        var query = Process.Start(new ProcessStartInfo("pnputil.exe", "/enum-devices /connected") { RedirectStandardOutput = true, UseShellExecute = false })!;
        string devices = query.StandardOutput.ReadToEnd();
        query.WaitForExit();
        seen = devices.Contains("VID_054C&PID_05C4", StringComparison.OrdinalIgnoreCase);
        Thread.Sleep(250);
    }

    Check(seen, "Windows enumerates a DualShock 4 (VID 054C, PID 05C4)", $"{watch.ElapsedMilliseconds} ms");
    pad.Dispose();
}

void RunHooks()
{
    const int F13 = 0x7C;
    using var pump = new InputPump();
    pump.Start(VirtualKeyCodes.F8);
    Thread.Sleep(200);

    Native.SendKey(F13, down: true);
    Thread.Sleep(60);
    Check((Native.GetAsyncKeyState(F13) & 0x8000) != 0, "unblocked key reaches Windows");
    Native.SendKey(F13, down: false);
    Thread.Sleep(60);

    pump.SetBlocking(true, [F13, VirtualKeyCodes.WheelUp], VirtualKeyCodes.F8);
    Native.SendKey(F13, down: true);
    Thread.Sleep(60);
    Check((Native.GetAsyncKeyState(F13) & 0x8000) == 0, "blocked key hidden from Windows");
    Check(pump.IsDown(F13), "blocked key still drives the pad");

    pump.SetBlocking(false, [F13, VirtualKeyCodes.WheelUp], VirtualKeyCodes.F8);
    Native.SendKey(F13, down: false);
    Thread.Sleep(60);
    Check(!pump.IsDown(F13), "release after blocking ends clears the key");

    pump.SetBlocking(true, [F13, VirtualKeyCodes.WheelUp], VirtualKeyCodes.F8);
    Native.SendKey(VirtualKeyCodes.F8, down: true);
    Thread.Sleep(60);
    Check((Native.GetAsyncKeyState(VirtualKeyCodes.F8) & 0x8000) != 0, "toggle key never blocked");
    Native.SendKey(VirtualKeyCodes.F8, down: false);

    Native.SendWheel(120);
    Thread.Sleep(10);
    Check(pump.IsDown(VirtualKeyCodes.WheelUp), "wheel notch becomes a short press");
    Thread.Sleep(120);
    Check(!pump.IsDown(VirtualKeyCodes.WheelUp), "wheel press ends on its own");

    Native.GetCursorPos(out Native.Point start);
    pump.ConsumeDelta();
    Native.SendMove(30, 0);
    Thread.Sleep(80);
    Native.GetCursorPos(out Native.Point after);
    (int dx, _) = pump.ConsumeDelta();
    pump.SetBlocking(false, [], VirtualKeyCodes.F8);
    Check(after.X == start.X && after.Y == start.Y, "cursor frozen while blocking", $"{start.X},{start.Y} -> {after.X},{after.Y}");
    Check(dx != 0, "raw mouse motion still reaches the stick", "dx " + dx);
}

int WaitForNewSlot(int previousMask)
{
    var watch = Stopwatch.StartNew();
    while (watch.ElapsedMilliseconds < 4000)
    {
        int fresh = Native.ConnectedSlots() & ~previousMask;
        for (int i = 0; i < 4; i++)
        {
            if ((fresh & (1 << i)) != 0)
                return i;
        }

        Thread.Sleep(50);
    }

    return -1;
}

Native.XInputState Drive(PadEngine engine, int slot, Action arrange)
{
    arrange();
    for (int i = 0; i < 10; i++)
    {
        engine.Tick(0.004f);
        Thread.Sleep(4);
    }

    Thread.Sleep(40);
    Native.XInputState state = default;
    var watch = Stopwatch.StartNew();
    int result;
    while ((result = Native.XInputGetState(slot, out state)) != 0 && watch.ElapsedMilliseconds < 1000)
        Thread.Sleep(10);

    if (result != 0)
        Console.WriteLine($"      XInputGetState({slot}) returned {result}");

    return result == 0 ? state : default;
}

static string Describe(Native.XInputState s) =>
    $"buttons 0x{s.Gamepad.Buttons:X4} LT {s.Gamepad.LeftTrigger} RT {s.Gamepad.RightTrigger} L ({s.Gamepad.ThumbLX},{s.Gamepad.ThumbLY}) R ({s.Gamepad.ThumbRX},{s.Gamepad.ThumbRY})";

sealed class FakeKeys : IKeyboardSource
{
    public HashSet<int> Down { get; } = new();

    public bool IsDown(int virtualKey)
    {
        lock (Down)
            return Down.Contains(virtualKey);
    }
}

sealed class FakeMouse : IMouseSource
{
    private int _x;
    private int _y;

    public void Add(int x, int y)
    {
        Interlocked.Add(ref _x, x);
        Interlocked.Add(ref _y, y);
    }

    public (int X, int Y) ConsumeDelta() => (Interlocked.Exchange(ref _x, 0), Interlocked.Exchange(ref _y, 0));
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public int Dx;
        [FieldOffset(12)] public int Dy;
        [FieldOffset(16)] public uint MouseData;
        [FieldOffset(20)] public uint MouseFlags;
        [FieldOffset(8)] public ushort Vk;
        [FieldOffset(12)] public uint KeyFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyInfoEx
    {
        public uint Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2;
    }

    [DllImport("xinput1_4.dll")]
    public static extern int XInputGetState(int index, out XInputState state);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static int ConnectedSlots()
    {
        int mask = 0;
        for (int i = 0; i < 4; i++)
        {
            if (XInputGetState(i, out _) == 0)
                mask |= 1 << i;
        }

        return mask;
    }

    public static void SendKey(int vk, bool down) =>
        SendInput(1, [new Input { Type = 1, Vk = (ushort)vk, KeyFlags = down ? 0u : 2u }], 40);

    public static void SendWheel(int delta) =>
        SendInput(1, [new Input { Type = 0, MouseData = (uint)delta, MouseFlags = 0x0800 }], 40);

    public static void SendMove(int dx, int dy) =>
        SendInput(1, [new Input { Type = 0, Dx = dx, Dy = dy, MouseFlags = 0x0001 }], 40);
}
