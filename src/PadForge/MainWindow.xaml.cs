using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PadForge.ViewModels;
using Windows.Graphics;
using Windows.System;

namespace PadForge;

public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly Action _onClosed;
    private bool _closed;

    public MainWindow(MainViewModel viewModel, Action onClosed)
    {
        ViewModel = viewModel;
        _onClosed = onClosed;
        InitializeComponent();
        Title = "PadForge";
        ConfigureShell();
        ViewModel.CaptureStarted += () => Root.Focus(FocusState.Programmatic);

        _timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += (_, _) => OnFrame();
        _timer.Start();
        Closed += OnClosed;
    }

    public MainViewModel ViewModel { get; }

    public IntPtr Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void OnFrame()
    {
        ViewModel.Pull();
        Preview.Apply(ViewModel.Frame);
        BusDot.Fill = ViewModel.BusReady ? Brush("ForgeGoodBrush") : ViewModel.BusMissing ? Brush("ForgeBadBrush") : Brush("ForgeMutedBrush");
        LinkDot.Fill = ViewModel.Connected ? Brush("ForgeGoodBrush") : Brush("ForgeMutedBrush");
        OutputDot.Fill = ViewModel.Sending ? Brush("ForgeGoodBrush") : ViewModel.Connected ? Brush("ForgeWarnBrush") : Brush("ForgeMutedBrush");
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ViewModel.IsCapturing)
            return;

        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.CancelCapture();
            e.Handled = true;
            return;
        }

        if (e.Key is VirtualKey.Back or VirtualKey.Delete)
        {
            ViewModel.TryCaptureKey(0);
            e.Handled = true;
            return;
        }

        if (ViewModel.TryCaptureKey((int)e.Key))
            e.Handled = true;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed)
            return;

        _closed = true;
        _timer.Stop();
        _onClosed();
    }

    private void ConfigureShell()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDrag);

        IntPtr hwnd = Handle;
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1180, 860));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 980;
            presenter.PreferredMinimumHeight = 700;
        }

        if (!AppWindowTitleBar.IsCustomizationSupported())
            return;

        AppWindowTitleBar titleBar = appWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        Windows.UI.Color background = Windows.UI.Color.FromArgb(255, 16, 18, 22);
        Windows.UI.Color foreground = Windows.UI.Color.FromArgb(255, 243, 245, 248);
        Windows.UI.Color muted = Windows.UI.Color.FromArgb(255, 167, 176, 192);
        Windows.UI.Color hover = Windows.UI.Color.FromArgb(255, 36, 40, 48);
        Windows.UI.Color pressed = Windows.UI.Color.FromArgb(255, 48, 54, 64);
        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = muted;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = muted;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
