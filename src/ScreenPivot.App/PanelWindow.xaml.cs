using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using ScreenPivot.App.Interop;
using ScreenPivot.App.Pages;
using ScreenPivot.App.Services;
using ScreenPivot.Core.Detection;
using ScreenPivot.Core.Diagnostics;
using ScreenPivot.Core.Interop;
using ScreenPivot.Core.Overlay;
using ScreenPivot.Core.Switching;
using Windows.Graphics;

namespace ScreenPivot.App;

/// <summary>
/// Borderless panel docked to a screen edge, topmost while visible and kept out of Alt+Tab and the taskbar.
/// Shows the summary; the gear swaps in the settings. Hides when another app takes the foreground (the user
/// clicked the Cloud PC or a host app), on Esc or with ✕. Our own dialogs (file picker) do not hide it.
/// </summary>
public sealed partial class PanelWindow : Window
{
    /// <summary>
    /// Right after opening over the full-screen RDP client the foreground can bounce back to it once; such a change
    /// is not the user clicking elsewhere, so it does not hide the panel.
    /// </summary>
    private static readonly TimeSpan OpenGrace = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// A foreground change counts as "the user went elsewhere" only if keyboard or mouse input happened this
    /// recently; otherwise another app stole the focus on its own (seen in the log: an app grabbing the
    /// foreground ~1.5 s after the panel opened) and the panel stays.
    /// </summary>
    private static readonly TimeSpan UserInputWindow = TimeSpan.FromMilliseconds(700);

    private readonly AppServices _services;
    private bool _exiting;
    private DateTimeOffset _shownAt;

    public PanelWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ScreenPivot.ico"));
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        AppWindow.Closing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true; // Alt+F4 hides; exit is in the tray menu
            HidePanel("alt-f4");
        };
        _services.ForegroundChanged += OnForegroundChanged;
    }

    public bool IsOpen => AppWindow.IsVisible;

    public void ShowPage(AppPage page)
    {
        Navigate(page);
        PlaceOnEdge();
        _shownAt = DateTimeOffset.Now;
        AppWindow.Show();
        _services.PanelOpen = true;
        Activate();
        bool focused = WindowSwitcher.ForceForeground(WinRT.Interop.WindowNative.GetWindowHandle(this));
        DiagnosticsLog.Write($"panel show page={page} focused={focused} foreground={ForegroundProcess()}");
    }

    public void HidePanel() => HidePanel("request");

    private void HidePanel(string reason)
    {
        if (AppWindow.IsVisible) DiagnosticsLog.Write($"panel hide reason={reason}");
        AppWindow.Hide();
        _services.PanelOpen = false;
    }

    private static string ForegroundProcess()
    {
        Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out uint pid);
        return pid == Environment.ProcessId ? "ScreenPivot" : RemoteSessionDetector.ProcessName(pid);
    }

    public void CloseForExit()
    {
        _exiting = true;
        _services.ForegroundChanged -= OnForegroundChanged;
        Close();
    }

    private void Navigate(AppPage page)
    {
        var type = page == AppPage.Settings ? typeof(SettingsPage) : typeof(DigestPage);
        ContentFrame.Navigate(type, _services);
        ContentFrame.BackStack.Clear();
    }

    public void PlaceOnEdge()
    {
        var settings = _services.Settings;
        var detection = _services.Detection;
        var monitors = detection.Monitors.Count > 0 ? detection.Monitors : RemoteSessionDetector.GetMonitors();
        // Automatic: the monitor under the cursor, i.e. where the user just clicked the pill or the tray icon.
        NativeUi.GetCursorPos(out var cursor);
        var monitor = settings.PanelMonitorIndex >= 0
            ? MonitorPicker.Pick(monitors, detection.Coverage, settings.PanelMonitorIndex)
            : MonitorPicker.AtPoint(monitors, cursor.X, cursor.Y) ?? MonitorPicker.Pick(monitors, detection.Coverage, settings.MonitorIndex);
        if (monitor is null) return;

        // Over the session the remote taskbar is what is visible, so use the whole monitor; on the host keep its taskbar.
        bool covered = monitor.Index < detection.Coverage.Count && detection.Coverage[monitor.Index] != CoverageKind.None;
        var area = covered ? monitor.Bounds : monitor.WorkArea;
        double scale = NativeUi.ScaleAt(area.Left + area.Width / 2, area.Top + area.Height / 2);
        var r = PanelLayout.Compute(area, settings.PanelEdge, scale);
        var rect = new RectInt32(r.Left, r.Top, r.Width, r.Height);
        // Twice: crossing to a monitor with another DPI makes Windows rescale the first attempt.
        AppWindow.MoveAndResize(rect);
        AppWindow.MoveAndResize(rect);
    }

    private void OnForegroundChanged(object? sender, EventArgs e)
    {
        if (!AppWindow.IsVisible) return;
        Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out uint pid);
        if (pid == Environment.ProcessId) return;
        if (DateTimeOffset.Now - _shownAt < OpenGrace)
        {
            DiagnosticsLog.Write($"panel ignored foreground bounce to {ForegroundProcess()}");
            return;
        }
        var sinceInput = NativeUi.SinceLastInput();
        if (sinceInput > UserInputWindow)
        {
            DiagnosticsLog.Write($"panel ignored foreground change to {ForegroundProcess()} without user input ({sinceInput.TotalMilliseconds:0} ms)");
            return;
        }
        HidePanel($"foreground={ForegroundProcess()}");
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        bool settings = e.SourcePageType == typeof(SettingsPage);
        BackButton.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Text = settings ? Loc.Get("Panel_SettingsTitle") : "ScreenPivot";
    }

    private void OnSettings(object sender, RoutedEventArgs e) => Navigate(AppPage.Settings);

    private void OnBack(object sender, RoutedEventArgs e) => Navigate(AppPage.Digest);

    private void OnHide(object sender, RoutedEventArgs e) => HidePanel("close-button");

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        HidePanel("esc");
    }
}
