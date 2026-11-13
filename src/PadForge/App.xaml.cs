using System.Diagnostics;
using Microsoft.UI.Xaml;
using PadForge.Core.Profiles;
using PadForge.Core.Runtime;
using PadForge.Output;
using PadForge.Platform;
using PadForge.ViewModels;

namespace PadForge;

public partial class App : Application
{
    private MainWindow? _window;
    private PadEngine? _engine;
    private InputPump? _input;
    private bool _shutDown;
    private Mutex? _singleInstance;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, eventArgs) => Debug.WriteLine(eventArgs.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Two copies would fight over the hotkey, the hooks and the virtual pad.
        _singleInstance = new Mutex(true, @"Local\PadForge.SingleInstance", out bool first);
        if (!first)
        {
            ForegroundWindow.ActivateExisting("PadForge");
            Exit();
            return;
        }

        TimerResolution.Begin();

        var store = new ProfileStore(ProfileStore.DefaultDirectory);
        Profile profile = store.LoadOrCreateDefault();
        var pad = new VigemGamepad();
        _input = new InputPump();
        _engine = new PadEngine(_input, _input, pad, profile.ToActive(), _input);
        _input.HotkeyPressed += () => _engine.RequestToggle();
        _input.Start(profile.HotkeyVirtualKey);
        _engine.Start();

        var viewModel = new MainViewModel(_engine, _input, _input, store, profile);
        _window = new MainWindow(viewModel, Shutdown);
        _window.Activate();
        viewModel.UseForegroundPause(_window.Handle);

        _ = ProbeBusAsync();
    }

    private async Task ProbeBusAsync()
    {
        BusProbeResult result = await Task.Run(VigemBusProbe.Check);
        _engine?.PublishBus(result.IsInstalled);
    }

    private void Shutdown()
    {
        if (_shutDown)
            return;

        _shutDown = true;
        _engine?.Dispose();
        _input?.Dispose();
        TimerResolution.End();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
    }
}
