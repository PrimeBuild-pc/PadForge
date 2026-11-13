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
/// GetAsyncKeyState. Input is observed, not swallowed, so the game still
/// receives the real keyboard and mouse.
/// </summary>
public sealed class InputPump : IMouseSource, IKeyboardSource, IHotkeyBinding, IDisposable
{
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

    public InputPump()
    {
        _bufferPin = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        _bufferPointer = _bufferPin.AddrOfPinnedObject();
        _windowProc = WindowProc;
    }

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
        if (virtualKey <= 0)
            return false;

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
        _ready.Set();

        while (GetMessage(out Msg message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

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
