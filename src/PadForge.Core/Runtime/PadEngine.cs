using System.Diagnostics;
using System.Threading.Channels;
using PadForge.Core.Input;
using PadForge.Core.Mapping;
using PadForge.Core.Output;

namespace PadForge.Core.Runtime;

/// <summary>
/// Owns the virtual pad on one high-priority thread. Call <see cref="Tick"/>
/// from tests; <see cref="Start"/> is the production loop. ViGEm calls stay
/// on that thread.
/// </summary>
public sealed class PadEngine : IDisposable
{
    private readonly IMouseSource _mouse;
    private readonly IKeyboardSource _keyboard;
    private readonly IVirtualGamepad _pad;
    private readonly MouseToStickConverter _rightStick = new();
    private readonly Channel<Action> _commands = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });
    private readonly CancellationTokenSource _cancellation = new();

    private Thread? _thread;
    private ActiveSettings _settings;
    private EngineSnapshot _snapshot = new();
    private string? _fault;
    private int _busState;
    private bool _wasSubmitting;
    private bool _disposed;
    private long _lastTimestamp;

    public PadEngine(IMouseSource mouse, IKeyboardSource keyboard, IVirtualGamepad pad, ActiveSettings settings)
    {
        _mouse = mouse;
        _keyboard = keyboard;
        _pad = pad;
        _settings = settings;
    }

    public EngineSnapshot Snapshot => _snapshot;

    public Func<bool>? CanSubmit { get; set; }

    public void Start()
    {
        if (_thread is not null)
            return;

        _lastTimestamp = Stopwatch.GetTimestamp();
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "PadForge.Engine",
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
    }

    public void PublishBus(bool installed)
    {
        Volatile.Write(ref _busState, installed ? 1 : 2);
    }

    public void RequestConnect() => Enqueue(Connect);

    public void RequestDisconnect() => Enqueue(Disconnect);

    public void RequestToggle() => Enqueue(Toggle);

    public void RequestCenter() => Enqueue(_rightStick.Reset);

    public void Apply(ActiveSettings settings, bool resetStick)
    {
        Enqueue(() =>
        {
            _settings = settings;
            if (resetStick)
                _rightStick.Reset();
        });
    }

    public void Tick(float deltaSeconds)
    {
        Drain();
        var (dx, dy) = _mouse.ConsumeDelta();
        ActiveSettings settings = _settings;
        ComposedInput composed = PadComposer.Compose(
            _rightStick,
            settings,
            dx,
            dy,
            _keyboard.IsDown,
            deltaSeconds);

        bool connected = _pad.IsConnected;
        bool allowSubmit = CanSubmit?.Invoke() ?? true;
        bool sending = false;
        if (connected)
        {
            if (allowSubmit)
            {
                _pad.Submit(composed.ToReport());
                _wasSubmitting = true;
                sending = true;
            }
            else if (_wasSubmitting)
            {
                _pad.Submit(GamepadReport.Neutral);
                _wasSubmitting = false;
            }
        }
        else
        {
            _wasSubmitting = false;
        }

        int busState = Volatile.Read(ref _busState);
        _snapshot = new EngineSnapshot
        {
            Frame = composed.ToFrame(),
            BusKnown = busState != 0,
            BusReady = busState == 1,
            Connected = connected,
            Sending = sending,
            Fault = _fault,
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellation.Cancel();
        if (_thread is not null && _thread.IsAlive && Thread.CurrentThread != _thread)
            _thread.Join(TimeSpan.FromSeconds(2));

        _pad.Dispose();
        _cancellation.Dispose();
    }

    private void Loop()
    {
        const double targetSeconds = 0.004;
        _lastTimestamp = Stopwatch.GetTimestamp();
        while (!_cancellation.IsCancellationRequested)
        {
            long now = Stopwatch.GetTimestamp();
            float dt = (float)((now - _lastTimestamp) / (double)Stopwatch.Frequency);
            _lastTimestamp = now;
            if (dt <= 0f || dt > 0.1f)
                dt = (float)targetSeconds;

            try
            {
                Tick(dt);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                _fault = "The controller loop hit an unexpected error.";
                TryDisconnectQuietly();
            }

            long spent = Stopwatch.GetTimestamp() - now;
            double remainMs = (targetSeconds * 1000d) - (spent * 1000d / Stopwatch.Frequency);
            if (remainMs > 1.2 && _cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(remainMs - 0.4)))
                break;

            long wake = now + (long)(targetSeconds * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < wake && !_cancellation.IsCancellationRequested)
                Thread.SpinWait(25);
        }
    }

    private void Toggle()
    {
        if (_pad.IsConnected)
            Disconnect();
        else
            Connect();
    }

    private void Connect()
    {
        if (_pad.IsConnected)
            return;

        int busState = Volatile.Read(ref _busState);
        if (busState != 1)
        {
            _fault = busState == 0
                ? "Still checking for ViGEmBus."
                : "ViGEmBus is not installed.";
            return;
        }

        try
        {
            _pad.Connect();
            _fault = null;
        }
        catch (VirtualGamepadException ex)
        {
            _fault = ex.Message;
            TryDisconnectQuietly();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _fault = "Could not create the virtual controller.";
            TryDisconnectQuietly();
        }
    }

    private void Disconnect()
    {
        _fault = null;
        if (!_pad.IsConnected)
            return;

        try
        {
            _pad.Submit(GamepadReport.Neutral);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        try
        {
            _pad.Disconnect();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _fault = "Could not remove the virtual controller.";
        }

        _wasSubmitting = false;
        _rightStick.Reset();
    }

    private void TryDisconnectQuietly()
    {
        try
        {
            if (_pad.IsConnected)
                _pad.Disconnect();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void Enqueue(Action action)
    {
        if (_disposed)
            return;

        _commands.Writer.TryWrite(action);
    }

    private void Drain()
    {
        while (_commands.Reader.TryRead(out Action? action))
            action();
    }
}
