using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using ScreenPivot.App.Services;
using ScreenPivot.Core.Sources;

namespace ScreenPivot.App;

public partial class App : Application
{
    public const string DemoArgument = "--demo";

    /// <summary>Opens the settings right away (used after restarting to apply a new language).</summary>
    public const string OpenSettingsArgument = "--open-settings";

    /// <summary>Public privacy policy (docs/store/privacy-policy.md, hosted). Empty hides the link in Settings.</summary>
    public const string PrivacyPolicyUrl = "https://mmoralesv.github.io/ScreenPivot/store/privacy-policy.html";

    private readonly bool _startedInBackground;
    private readonly bool _demo;
    private readonly bool _openSettings;
    private DispatcherQueue? _dispatcher;
    private PanelWindow? _window;

    public App(string[] args)
    {
        _startedInBackground = args.Contains(StartupService.BackgroundArgument);
        _demo = args.Contains(DemoArgument);
        _openSettings = args.Contains(OpenSettingsArgument);
        InitializeComponent();
        // Lives in the notification area: closing the window must not end the process.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    public static new App? Current => Application.Current as App;

    public AppServices Services { get; private set; } = null!;

    /// <summary>HWND of the panel window, for pickers that need an owner; Zero before it is created.</summary>
    internal IntPtr PanelHandle => _window is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(_window);

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Services = new AppServices(_dispatcher, _demo);
        Services.OpenRequested += (_, page) => ShowPanel(page);
        Services.ExitRequested += (_, _) => Shutdown();
        await Services.StartAsync();

        bool needsSetup = !Services.Settings.OnboardingCompleted || Services.Toasts.Access != ToastAccess.Allowed;
        if (_openSettings) ShowPanel(AppPage.Settings);
        else if (_demo) ShowPanel(AppPage.Digest);
        else if (needsSetup) ShowPanel(AppPage.Settings);
        else if (!_startedInBackground) ShowPanel(AppPage.Digest);
    }

    /// <summary>Another launch was redirected here (called on a background thread).</summary>
    internal void OnRedirectedActivation() => _dispatcher?.TryEnqueue(() => ShowPanel(AppPage.Digest));

    public void ShowPanel(AppPage page)
    {
        _window ??= new PanelWindow(Services);
        _window.ShowPage(page);
    }

    internal void RepositionPanel()
    {
        if (_window?.IsOpen == true) _window.PlaceOnEdge();
    }

    /// <summary>
    /// Restarts to apply a new UI language (MRT and XAML resources are fixed once loaded). The single-instance key is
    /// released first so the new process does not redirect back to this one.
    /// </summary>
    internal void Restart()
    {
        var args = string.Join(" ", new[] { _demo ? DemoArgument : "", OpenSettingsArgument }.Where(a => a.Length > 0));
        _window?.CloseForExit();
        Services.Dispose();
        AppInstance.GetCurrent().UnregisterKey();
        AppInstance.Restart(args); // only returns if Windows refused; then start a new process ourselves
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = false });
        Exit();
    }

    private void Shutdown()
    {
        _window?.CloseForExit();
        Services.Dispose();
        Exit();
    }
}
