using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PadForge.Core.Input;
using PadForge.ViewModels;
using PadForge.Views;
using Windows.Graphics;
using Windows.System;

namespace PadForge;

public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly Action _onClosed;
    private readonly Dictionary<string, FrameworkElement> _pages = new();
    private IFramePage? _current;
    private bool _closed;

    public MainWindow(MainViewModel viewModel, Action onClosed)
    {
        ViewModel = viewModel;
        _onClosed = onClosed;
        InitializeComponent();
        Title = "PadForge";
        ConfigureShell();
        ViewModel.CaptureStarted += () => Root.Focus(FocusState.Programmatic);
        Show("overview");

        _timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) => OnFrame();
        _timer.Start();
        Closed += OnClosed;
    }

    public MainViewModel ViewModel { get; }

    public IntPtr Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && PageHost is not null)
            Show(tag);
    }

    private void Show(string tag)
    {
        if (!_pages.TryGetValue(tag, out FrameworkElement? page))
        {
            page = tag switch
            {
                "controller" => new ControllerPage(ViewModel),
                "aim" => new AimPage(ViewModel),
                "mapping" => new MappingPage(ViewModel),
                "profiles" => new ProfilesPage(ViewModel),
                "settings" => new SettingsPage(ViewModel),
                _ => new OverviewPage(ViewModel),
            };
            _pages[tag] = page;
            PageHost.Children.Add(page);
        }

        foreach (FrameworkElement other in _pages.Values)
            other.Visibility = other == page ? Visibility.Visible : Visibility.Collapsed;

        _current = page as IFramePage;
    }

    private void OnFrame()
    {
        ViewModel.Pull();
        _current?.Render(ViewModel);
        BusDot.Fill = ViewModel.BusReady ? Brush("ForgeGoodBrush") : ViewModel.BusMissing ? Brush("ForgeBadBrush") : Brush("ForgeSubtleBrush");
        SideDot.Fill = ViewModel.Sending ? Brush("ForgeGoodBrush") : ViewModel.Connected ? Brush("ForgeWarnBrush") : Brush("ForgeSubtleBrush");
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

    private void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.IsCapturing)
            return;

        int delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        if (delta != 0 && ViewModel.TryCaptureKey(delta > 0 ? VirtualKeyCodes.WheelUp : VirtualKeyCodes.WheelDown))
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
        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1280, 860));
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "PadForge.ico");
        if (File.Exists(icon))
            appWindow.SetIcon(icon);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1040;
            presenter.PreferredMinimumHeight = 700;
        }

        if (!AppWindowTitleBar.IsCustomizationSupported())
            return;

        AppWindowTitleBar titleBar = appWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        Windows.UI.Color transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        Windows.UI.Color foreground = Windows.UI.Color.FromArgb(255, 242, 243, 247);
        Windows.UI.Color muted = Windows.UI.Color.FromArgb(255, 146, 152, 168);
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = muted;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 35, 37, 47);
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 46, 48, 60);
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
