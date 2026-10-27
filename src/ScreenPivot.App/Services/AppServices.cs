using Microsoft.UI.Dispatching;
using ScreenPivot.App.Presentation;
using ScreenPivot.Core;
using ScreenPivot.Core.Detection;
using ScreenPivot.Core.Diagnostics;
using ScreenPivot.Core.Digest;
using ScreenPivot.Core.Interop;
using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;
using ScreenPivot.Core.Sources;
using ScreenPivot.Core.Storage;
using ScreenPivot.Core.Switching;

namespace ScreenPivot.App.Services;

public enum AppPage
{
    Digest,
    Settings,
}

/// <summary>
/// Composition root and coordinator. Everything here runs on the UI thread; toast events from the polling
/// thread are marshalled through the dispatcher before reaching the hub.
/// </summary>
public sealed class AppServices : IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly SettingsStore _settingsStore;
    private readonly RemoteSessionMonitor _monitor = new();
    private readonly OverlayController _overlay;
    private readonly WindowActivitySource _windowActivity;
    private TrayIcon? _tray;
    /// <summary>Session window we minimized to show a host app; the "back" pill stays until it is restored.</summary>
    private IntPtr _parked;

    /// <param name="demo">
    /// Sample data in a separate folder and no real sources: for Store screenshots and certification testers.
    /// </param>
    public AppServices(DispatcherQueue dispatcher, bool demo = false)
    {
        _dispatcher = dispatcher;
        IsDemo = demo;
        var dataDirectory = demo ? Path.Combine(SettingsStore.DefaultDirectory, "demo") : SettingsStore.DefaultDirectory;
        DiagnosticsLog.Initialize(dataDirectory);
        _settingsStore = new SettingsStore(dataDirectory);
        Settings = _settingsStore.Load();
        Digest = new DigestStore(dataDirectory, TimeSpan.FromHours(Settings.RetentionHours));
        Hub = new EventHub(() => Settings, Digest);
        Toasts = new ToastSource();
        _windowActivity = new WindowActivitySource(() => Settings);
        _overlay = new OverlayController(() => Settings, dispatcher);
    }

    public AppSettings Settings { get; private set; }

    public bool IsDemo { get; }

    public DigestStore Digest { get; }

    public EventHub Hub { get; }

    public ToastSource Toasts { get; }

    public DetectionResult Detection => _monitor.Current;

    /// <summary>Events of the last finished remote session, for the digest.</summary>
    public IReadOnlyList<HostEvent> LastSession { get; private set; } = Array.Empty<HostEvent>();

    /// <summary>Message for the user about the last switch attempt; empty when it went fine.</summary>
    public string Notice { get; private set; } = "";

    public bool HasSession => WindowSwitcher.IsAlive(_parked) || WindowSwitcher.FindSessionWindow() != IntPtr.Zero;

    public bool IsParked => _parked != IntPtr.Zero;

    private bool _panelOpen;

    /// <summary>Set by the panel window; while it is visible the overlay stays out of its way.</summary>
    public bool PanelOpen
    {
        get => _panelOpen;
        set
        {
            if (_panelOpen == value) return;
            _panelOpen = value;
            UpdateOverlay();
        }
    }

    /// <summary>Remote state, pending events, access or settings changed; pages refresh on it.</summary>
    public event EventHandler? StateChanged;

    public event EventHandler<AppPage>? OpenRequested;

    /// <summary>Any window, ours included, became the foreground one; the panel hides when it is another app's.</summary>
    public event EventHandler? ForegroundChanged;

    public event EventHandler? ExitRequested;

    public async Task StartAsync()
    {
        _tray = new TrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ScreenPivot.ico"));
        _tray.Paused = Settings.Paused;
        _tray.Command += OnTrayCommand;

        _overlay.OpenEventRequested += (_, e) => OpenEventApp(e);
        _overlay.PanelRequested += (_, _) =>
        {
            Hub.MarkReviewed(DateTimeOffset.Now); // the panel shows them
            OpenRequested?.Invoke(this, AppPage.Digest);
            Refresh();
        };
        _overlay.ReturnRequested += (_, _) => ReturnToRemote();
        Hub.Accepted += OnAccepted;
        Toasts.EventReceived += (_, e) => _dispatcher.TryEnqueue(() => Hub.Ingest(e));
        // Flashes and title changes are only news while the session hides the host; on the host they are visible.
        _windowActivity.EventReceived += (_, e) =>
        {
            if (Hub.IsRemote) Hub.Ingest(e);
        };
        if (IsDemo) SeedDemo();
        else _windowActivity.Start();

        _monitor.Changed += OnCoverageChanged;
        _monitor.ForegroundChanged += (_, _) =>
        {
            UpdateOverlay();
            ForegroundChanged?.Invoke(this, EventArgs.Empty);
        };
        _monitor.Start();

        if (!IsDemo) await Toasts.StartAsync();
        UpdateTooltip();
    }

    public async Task<ToastAccess> RequestNotificationAccessAsync()
    {
        var access = await Toasts.RequestAccessAsync();
        if (access == ToastAccess.Allowed && !IsDemo) await Toasts.StartAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return access;
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        _settingsStore.Save(settings);
        Digest.Retention = TimeSpan.FromHours(Math.Max(1, settings.RetentionHours));
        if (_tray is not null) _tray.Paused = settings.Paused;
        // Re-evaluate remote mode (IncludeMaximized) and overlay placement right away.
        OnCoverageChanged(this, _monitor.Current);
    }

    public void MarkReviewed()
    {
        Hub.MarkReviewed(DateTimeOffset.Now);
        Refresh();
    }

    public void ShowTestBanner()
    {
        var sample = new HostEvent("test", "ScreenPivot", "Banner de prueba", "Haz clic para cerrarlo. Debe verse encima del Cloud PC.",
            DateTimeOffset.Now, EventOrigin.Test) { Priority = Priority.Critical, Reason = "Prueba" };
        _overlay.ShowTest(sample);
    }

    /// <summary>Brings the event's app to the front (minimizing the session first if configured).</summary>
    public SwitchResult OpenEventApp(HostEvent e)
    {
        var session = WindowSwitcher.FindSessionWindow();
        if (Settings.SwitchMode == SwitchMode.MinimizeRemote) Park(session);

        var result = WindowSwitcher.FocusApp(e.AppId, e.App, e.WindowHandle);
        if (result == SwitchResult.FocusDenied && !IsParked)
        {
            // Windows refused the foreground; getting the session out of the way usually unblocks it.
            Park(session);
            result = WindowSwitcher.FocusApp(e.AppId, e.App, e.WindowHandle);
        }

        Notice = result switch
        {
            SwitchResult.NotFound => $"No encontré una ventana de {e.App} ni pude abrirla. Ábrela desde el equipo.",
            SwitchResult.FocusDenied => $"Windows no permitió traer {e.App} al frente. Búscala en la barra de tareas.",
            _ => "",
        };
        if (Notice.Length > 0) OpenRequested?.Invoke(this, AppPage.Digest);
        Refresh();
        return result;
    }

    public bool ReturnToRemote()
    {
        var target = WindowSwitcher.IsAlive(_parked) ? _parked : WindowSwitcher.FindSessionWindow();
        _parked = IntPtr.Zero;
        var ok = WindowSwitcher.Focus(target);
        Notice = ok ? "" : "No encontré la sesión del Cloud PC o Windows no permitió activarla.";
        Refresh();
        return ok;
    }

    /// <summary>Minimizes the session so the whole host desktop is visible; the "back" pill stays on screen.</summary>
    public void MinimizeRemote()
    {
        Park(WindowSwitcher.FindSessionWindow());
        Refresh();
    }

    /// <summary>Replaces the demo history with a fictional finished remote session.</summary>
    private void SeedDemo()
    {
        Digest.Clear();
        var now = DateTimeOffset.Now;
        var events = DemoData.Session(now).Select(e => e with { ReceivedWhileRemote = true }).ToList();
        foreach (var e in events) Digest.Add(e);
        LastSession = events;
    }

    private void Park(IntPtr session)
    {
        if (session == IntPtr.Zero) return;
        _parked = session;
        WindowSwitcher.Minimize(session);
    }

    private void OnCoverageChanged(object? sender, DetectionResult detection)
    {
        bool remote = detection.IsRemote(Settings.IncludeMaximized);
        if (remote != Hub.IsRemote) DiagnosticsLog.Write($"remote={remote} coverage={detection.CoverageKey} parked={IsParked}");
        if (remote && !Hub.IsRemote)
        {
            _parked = IntPtr.Zero;
            Hub.EnterRemote(DateTimeOffset.Now);
        }
        else if (!remote && Hub.IsRemote)
        {
            LastSession = Hub.ExitRemote();
            // Minimized by us to look at a host app: the user is coming back, no digest yet.
            if (!IsParked && Settings.ShowDigestOnReturn && !Settings.Paused && Hub.Pending().Count > 0)
                OpenRequested?.Invoke(this, AppPage.Digest);
        }
        Refresh();
    }

    private void OnAccepted(object? sender, HostEvent e)
    {
        if (e.ReceivedWhileRemote)
        {
            _overlay.Notify(e, Hub.Pending());
            if (Settings.SoundEnabled && !Settings.Paused)
                SoundPlayer.Play(Settings.Sounds.For(e.Priority));
        }
        UpdateTooltip();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTrayCommand(object? sender, TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.OpenDigest:
                OpenRequested?.Invoke(this, AppPage.Digest);
                break;
            case TrayCommand.OpenSettings:
                OpenRequested?.Invoke(this, AppPage.Settings);
                break;
            case TrayCommand.ReturnToRemote:
                ReturnToRemote();
                break;
            case TrayCommand.MinimizeRemote:
                MinimizeRemote();
                break;
            case TrayCommand.TogglePause:
                Settings.Paused = !Settings.Paused;
                SaveSettings(Settings);
                break;
            case TrayCommand.Exit:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void Refresh()
    {
        UpdateOverlay();
        UpdateTooltip();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateOverlay()
    {
        // The user restored or closed the session themselves.
        if (IsParked && (!WindowSwitcher.IsAlive(_parked) || !Win32.IsIconic(_parked))) _parked = IntPtr.Zero;
        bool hostForeground = !RemoteSessionDetector.IsRemoteWindow(Win32.GetForegroundWindow());
        _overlay.Update(Hub.IsRemote, hostForeground, IsParked, _panelOpen, _monitor.Current, Hub.Pending());
    }

    private void UpdateTooltip()
    {
        if (_tray is null) return;
        _tray.HasSession = HasSession;
        var pending = Hub.Pending();
        var state = Settings.Paused ? "en pausa" : Hub.IsRemote ? "Cloud PC a pantalla completa" : "en el equipo";
        _tray.SetTooltip(pending.Count == 0 ? $"ScreenPivot · {state}" : $"ScreenPivot · {RulesSummarizer.Ticker(pending)}");
    }

    public void Dispose()
    {
        _monitor.Dispose();
        Toasts.Dispose();
        _windowActivity.Dispose();
        _overlay.Dispose();
        _tray?.Dispose();
    }
}
