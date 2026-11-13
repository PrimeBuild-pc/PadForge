using System.Runtime.InteropServices;
using PadForge.Core.Input;

namespace PadForge.Platform;

public interface IHotkeyBinding
{
    void SetHotkey(int virtualKey);

    string? HotkeyStatus { get; }
}

/// <summary>
/// Reads relative mouse deltas with Raw Input and keyboard state with
/// GetAsyncKeyState. While blocking is on, low-level hooks swallow mapped keys,
/// mapped clicks and cursor movement so Windows and the game do not see them;
/// swallowed presses are tracked here because GetAsyncKeyState no longer sees
/// them. Raw Input still reports mouse motion, which keeps the right stick fed.
/// </summary>
public sealed class InputPump : IMouseSource, IKeyboardSource, IHotkeyBinding, IInputBlocker, IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int KeyStates = 0x103;
    private static readonly long WheelPulseTicks = System.Diagnostics.Stopwatch.Frequency / 20;

    private const int HotkeyId = 1;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint WmInput = 0x00FF;
    private const uint WmAppHotkey = 0x8001;
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100;
    private const uint ModNorepeat = 0x4000;
    private const ushort HidUsagePageGeneric = 0x01;
    private const ushort HidUsageMouse = 0x02;

    private readonly byte[] _buffer = new byte[256];
    private readonly GCHandle _bufferPin;
    private readonly IntPtr _bufferPointer;
    private readonly WndProc _windowProc;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly string _className = "PadForge.Input." + Environment.ProcessId;

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hwnd;
    private int _dx;
    private int _dy;
    private int _pendingHotkey = VirtualKeyCodes.F8;
    private string? _hotkeyStatus;
    private bool _disposed;

    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private readonly bool[] _swallowed = new bool[KeyStates];
    private readonly long[] _wheelUntil = new long[2];
    private bool[] _blockTable = new bool[KeyStates];
    private int[]? _blockKeys;
    private int _blockPass = -1;
    private volatile bool _blocking;

    public InputPump()
    {
        _bufferPin = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        _bufferPointer = _bufferPin.AddrOfPinnedObject();
        _windowProc = WindowProc;
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
    }

    public bool IsBlocking => _blocking;

    public void SetBlocking(bool active, int[] keys, int passThroughKey)
    {
        if (!ReferenceEquals(keys, _blockKeys) || passThroughKey != _blockPass)
        {
            var table = new bool[KeyStates];
            foreach (int key in keys)
            {
                if (key > 0 && key < KeyStates && key != passThroughKey)
                    table[key] = true;
            }

            // The hook reports left/right modifiers; a binding saved as plain Shift, Ctrl or Alt covers both.
            for (int generic = 0x10; generic <= 0x12; generic++)
            {
                if (!table[generic])
                    continue;
                int left = LeftOf(generic);
                table[left] = left != passThroughKey;
                table[left + 1] = left + 1 != passThroughKey;
            }

            Volatile.Write(ref _blockTable, table);
            _blockKeys = keys;
            _blockPass = passThroughKey;
        }

        _blocking = active;
    }

    private static int LeftOf(int generic) => generic switch { 0x10 => 0xA0, 0x11 => 0xA2, _ => 0xA4 };

    public event Action? HotkeyPressed;

    public string? HotkeyStatus => Volatile.Read(ref _hotkeyStatus);

    public void Start(int hotkey)
    {
        if (_thread is not null)
            return;

        Volatile.Write(ref _pendingHotkey, hotkey);
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "PadForge.Input",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    public void SetHotkey(int virtualKey)
    {
        Volatile.Write(ref _pendingHotkey, virtualKey);
        if (_hwnd != IntPtr.Zero)
            PostMessage(_hwnd, WmAppHotkey, IntPtr.Zero, IntPtr.Zero);
    }

    public (int X, int Y) ConsumeDelta()
    {
        return (Interlocked.Exchange(ref _dx, 0), Interlocked.Exchange(ref _dy, 0));
    }

    public bool IsDown(int virtualKey)
    {
        if (virtualKey <= 0 || virtualKey >= KeyStates)
            return false;

        if (VirtualKeyCodes.IsWheel(virtualKey))
            return System.Diagnostics.Stopwatch.GetTimestamp() < Volatile.Read(ref _wheelUntil[virtualKey - VirtualKeyCodes.WheelUp]);

        if (_swallowed[virtualKey])
            return true;

        if (virtualKey is >= 0x10 and <= 0x12 && (_swallowed[LeftOf(virtualKey)] || _swallowed[LeftOf(virtualKey) + 1]))
            return true;

        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_threadId != 0)
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

        if (_thread is not null && _thread.IsAlive && Thread.CurrentThread != _thread)
            _thread.Join(TimeSpan.FromSeconds(2));

        if (_bufferPin.IsAllocated)
            _bufferPin.Free();

        _ready.Dispose();
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        IntPtr procedure = Marshal.GetFunctionPointerForDelegate(_windowProc);
        var windowClass = new WndClass
        {
            lpfnWndProc = procedure,
            hInstance = GetModuleHandle(null),
            lpszClassName = _className,
        };

        ushort atom = RegisterClassW(ref windowClass);
        if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
        {
            Volatile.Write(ref _hotkeyStatus, "Mouse capture failed to start.");
            _ready.Set();
            return;
        }

        _hwnd = CreateWindowExW(
            0,
            _className,
            "PadForge.Input",
            0,
            0, 0, 0, 0,
            new IntPtr(-3),
            IntPtr.Zero,
            windowClass.hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Volatile.Write(ref _hotkeyStatus, "Mouse capture failed to start.");
            _ready.Set();
            return;
        }

        RegisterMouse();
        ApplyHotkey();
        _keyboardHook = SetWindowsHookExW(WhKeyboardLl, _keyboardProc, windowClass.hInstance, 0);
        _mouseHook = SetWindowsHookExW(WhMouseLl, _mouseProc, windowClass.hInstance, 0);
        _ready.Set();

        while (GetMessage(out Msg message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        _blocking = false;
        if (_keyboardHook != IntPtr.Zero)
            UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero)
            UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
        Array.Clear(_swallowed);

        UnregisterHotKey(_hwnd, HotkeyId);
        DestroyWindow(_hwnd);
        UnregisterClassW(_className, windowClass.hInstance);
        _hwnd = IntPtr.Zero;
    }

    private void RegisterMouse()
    {
        var device = new RawInputDevice
        {
            usUsagePage = HidUsagePageGeneric,
            usUsage = HidUsageMouse,
            dwFlags = RidevInputSink,
            hwndTarget = _hwnd,
        };

        if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            Volatile.Write(ref _hotkeyStatus, "Mouse capture failed to start.");
    }

    private void ApplyHotkey()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        UnregisterHotKey(_hwnd, HotkeyId);
        int virtualKey = Volatile.Read(ref _pendingHotkey);
        if (virtualKey is < 0x08 or > 0xFE || VirtualKeyCodes.IsMouseButton(virtualKey))
        {
            Volatile.Write(ref _hotkeyStatus, "Choose a keyboard key for the toggle.");
            return;
        }

        if (RegisterHotKey(_hwnd, HotkeyId, ModNorepeat, (uint)virtualKey))
            Volatile.Write(ref _hotkeyStatus, null);
        else
            Volatile.Write(ref _hotkeyStatus, "That hotkey is already in use.");
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmInput:
                ReadMouse(lParam);
                return IntPtr.Zero;
            case WmHotkey when wParam.ToInt32() == HotkeyId:
                HotkeyPressed?.Invoke();
                return IntPtr.Zero;
            case WmAppHotkey:
                ApplyHotkey();
                return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ReadMouse(IntPtr handle)
    {
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint size = 0;
        GetRawInputData(handle, RidInput, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > _buffer.Length)
            return;

        if (GetRawInputData(handle, RidInput, _bufferPointer, ref size, headerSize) == uint.MaxValue)
            return;

        if ((uint)Marshal.ReadInt32(_bufferPointer) != 0)
            return;

        int mouse = (int)headerSize;
        ushort flags = (ushort)Marshal.ReadInt16(_bufferPointer, mouse);
        if ((flags & 0x01) != 0)
            return;

        int x = Marshal.ReadInt32(_bufferPointer, mouse + 12);
        int y = Marshal.ReadInt32(_bufferPointer, mouse + 16);
        if (x == 0 && y == 0)
            return;

        Interlocked.Add(ref _dx, x);
        Interlocked.Add(ref _dy, y);
    }

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int virtualKey = Marshal.ReadInt32(lParam);
            uint message = (uint)wParam.ToInt64();
            bool down = message is 0x0100 or 0x0104;
            if (virtualKey > 0 && virtualKey < KeyStates && Swallow(virtualKey, down))
                return (IntPtr)1;
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            uint message = (uint)wParam.ToInt64();
            int high = Marshal.ReadInt32(lParam, 8) >> 16;
            if (message == 0x0200)
            {
                // Freeze the cursor; Raw Input still delivers the motion to the stick.
                if (_blocking)
                    return (IntPtr)1;
            }
            else if (message == 0x020A)
            {
                int virtualKey = (short)high > 0 ? VirtualKeyCodes.WheelUp : VirtualKeyCodes.WheelDown;
                long until = System.Diagnostics.Stopwatch.GetTimestamp() + WheelPulseTicks;
                Volatile.Write(ref _wheelUntil[virtualKey - VirtualKeyCodes.WheelUp], until);
                if (_blocking && Volatile.Read(ref _blockTable)[virtualKey])
                    return (IntPtr)1;
            }
            else
            {
                int xButton = (high & 0xFFFF) == 2 ? VirtualKeyCodes.MouseX2 : VirtualKeyCodes.MouseX1;
                (int virtualKey, bool down) = message switch
                {
                    0x0201 => (VirtualKeyCodes.MouseLeft, true),
                    0x0202 => (VirtualKeyCodes.MouseLeft, false),
                    0x0204 => (VirtualKeyCodes.MouseRight, true),
                    0x0205 => (VirtualKeyCodes.MouseRight, false),
                    0x0207 => (VirtualKeyCodes.MouseMiddle, true),
                    0x0208 => (VirtualKeyCodes.MouseMiddle, false),
                    0x020B => (xButton, true),
                    0x020C => (xButton, false),
                    _ => (0, false),
                };
                if (virtualKey != 0 && Swallow(virtualKey, down))
                    return (IntPtr)1;
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>
    /// A release is swallowed only when its press was, so a key held while
    /// blocking switches on or off never sticks down in Windows.
    /// </summary>
    private bool Swallow(int virtualKey, bool down)
    {
        if (!down)
        {
            if (!_swallowed[virtualKey])
                return false;
            _swallowed[virtualKey] = false;
            return true;
        }

        if (_swallowed[virtualKey])
            return true;

        if (!_blocking || !Volatile.Read(ref _blockTable)[virtualKey])
            return false;

        _swallowed[virtualKey] = true;
        return true;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int hookId, HookProc procedure, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint deviceCount, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WndClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
