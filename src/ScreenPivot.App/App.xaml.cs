using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ScreenPivot.App.Services;
using ScreenPivot.Core.Sources;

namespace ScreenPivot.App;

public partial class App : Application
{
    public const string DemoArgument = "--demo";

    /// <summary>Public privacy policy (docs/store/privacy-policy.md, hosted). Empty hides the link in Settings.</summary>
    public const string PrivacyPolicyUrl = "";

    private readonly bool _startedInBackground;
    private readonly bool _demo;
    private DispatcherQueue? _dispatcher;
    private PanelWindow? _window;

    public App(string[] args)
    {
        _startedInBackground = args.Contains(StartupService.BackgroundArgument);
        _demo = args.Contains(DemoArgument);
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
        if (_demo) ShowPanel(AppPage.Digest);
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

    private void Shutdown()
    {
        _window?.CloseForExit();
        Services.Dispose();
        Exit();
    }
}
