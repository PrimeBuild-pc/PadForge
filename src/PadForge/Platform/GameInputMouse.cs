using System.Runtime.InteropServices;

namespace PadForge.Platform;

/// <summary>Receives relative mouse movement from the installed GameInput v4 runtime.</summary>
internal sealed class GameInputMouse : IDisposable
{
    private static readonly Guid InterfaceId = new("F2DAAE4C-4D21-4250-8953-4F791E6279AB");
    private const string RuntimeName = "GameInputRedist.dll";
    private const uint MouseKind = 0x20;
    private const uint RelativePosition = 0x02;

    private readonly IntPtr _input;
    private readonly Action<int, int> _addDelta;
    private readonly ReadingCallback _callback;
    private readonly EnableBackgroundInput _enableBackground;
    private readonly UnregisterCallback _unregister;
    private readonly Dictionary<IntPtr, (long X, long Y)> _positions = new();
    private GetMouseState? _getMouseState;
    private GetDevice? _getDevice;
    private ulong _token;
    private bool _disposed;

    private GameInputMouse(IntPtr input, Action<int, int> addDelta)
    {
        _input = input;
        _addDelta = addDelta;
        _callback = OnReading;
        // IGameInput v4 vtable from Microsoft.GameInput 4.0.306 (native/include/GameInput.h).
        _enableBackground = Method<EnableBackgroundInput>(input, 13);
        _unregister = Method<UnregisterCallback>(input, 9);
    }

    public static GameInputMouse? TryStart(Action<int, int> addDelta)
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string installedPath = Path.Combine(programFiles, "Microsoft GameInput", "x64", RuntimeName);
        string systemPath = Path.Combine(Environment.SystemDirectory, RuntimeName);
        return TryStart(installedPath, addDelta) ?? TryStart(systemPath, addDelta);
    }

    private static GameInputMouse? TryStart(string path, Action<int, int> addDelta)
    {
        if (!NativeLibrary.TryLoad(path, out IntPtr library))
            return null;

        if (!NativeLibrary.TryGetExport(library, "GameInputInitialize", out IntPtr export))
        {
            NativeLibrary.Free(library);
            return null;
        }

        var initialize = Marshal.GetDelegateForFunctionPointer<Initialize>(export);
        Guid iid = InterfaceId;
        if (initialize(in iid, out IntPtr input) < 0 || input == IntPtr.Zero)
        {
            NativeLibrary.Free(library);
            return null;
        }

        // Keep the initialized process-wide runtime loaded until exit.
        var mouse = new GameInputMouse(input, addDelta);
        var register = Method<RegisterReadingCallback>(input, 7);
        mouse._enableBackground(input, true);
        if (register(input, IntPtr.Zero, MouseKind, IntPtr.Zero, mouse._callback, out mouse._token) >= 0)
            return mouse;

        mouse.Dispose();
        return null;
    }

    private void OnReading(ulong token, IntPtr context, IntPtr reading)
    {
        // GameInput serializes reading callbacks. These slots use IGameInputReading v4.
        _getMouseState ??= Method<GetMouseState>(reading, 14);
        if (!_getMouseState(reading, out MouseState mouse) || (mouse.Positions & RelativePosition) == 0)
            return;

        _getDevice ??= Method<GetDevice>(reading, 5);
        _getDevice(reading, out IntPtr device);
        if (device == IntPtr.Zero)
            return;

        // Positions are cumulative per device. Hold one reference for each baseline so a
        // newly connected mouse can't inherit the baseline of a recycled interface pointer.
        var position = (mouse.X, mouse.Y);
        if (_positions.TryAdd(device, position))
            return;

        var previous = _positions[device];
        _positions[device] = position;
        Marshal.Release(device);
        long dx = mouse.X - previous.X;
        long dy = mouse.Y - previous.Y;
        if (dx is >= int.MinValue and <= int.MaxValue && dy is >= int.MinValue and <= int.MaxValue)
            _addDelta((int)dx, (int)dy);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        // Unregistering off the callback thread waits for any callback in flight.
        if (_token != 0)
            _unregister(_input, _token);
        _enableBackground(_input, false);
        foreach (IntPtr device in _positions.Keys)
            Marshal.Release(device);
        Marshal.Release(_input);
        GC.KeepAlive(_callback);
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Initialize(in Guid iid, out IntPtr input);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void EnableBackgroundInput(IntPtr input, [MarshalAs(UnmanagedType.I1)] bool enabled);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int RegisterReadingCallback(IntPtr input, IntPtr device, uint kind, IntPtr context,
        ReadingCallback callback, out ulong token);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool UnregisterCallback(IntPtr input, ulong token);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ReadingCallback(ulong token, IntPtr context, IntPtr reading);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GetDevice(IntPtr reading, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool GetMouseState(IntPtr reading, out MouseState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseState
    {
        public uint Buttons;
        public uint Positions;
        public long X;
        public long Y;
        public long AbsoluteX;
        public long AbsoluteY;
        public long WheelX;
        public long WheelY;
    }
}
