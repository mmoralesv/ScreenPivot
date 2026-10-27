using Microsoft.UI.Dispatching;
using ScreenPivot.App.Services;
using ScreenPivot.Core.Detection;
using ScreenPivot.Core.Diagnostics;
using ScreenPivot.Core.Digest;
using ScreenPivot.Core.Model;
using ScreenPivot.Core.Overlay;
using ScreenPivot.Core.Rules;

namespace ScreenPivot.App.Presentation;

/// <summary>
/// Keeps the overlay in sync with the remote state (what to show is decided by <see cref="OverlayPlanner"/>):
/// Critical → sticky banner; High → banner for a few seconds; in the session → ticker or quick access;
/// on the host while a session exists → "back to the Cloud PC".
/// </summary>
internal sealed class OverlayController : IDisposable
{
    private static readonly TimeSpan ReassertInterval = TimeSpan.FromSeconds(2);

    private readonly Func<AppSettings> _settings;
    private readonly OverlayWindow _window = new();
    private readonly DispatcherQueueTimer _bannerTimer;
    private readonly DispatcherQueueTimer _reassertTimer;
    private DetectionResult _detection = DetectionResult.Empty;
    private IReadOnlyList<HostEvent> _pending = Array.Empty<HostEvent>();
    private HostEvent? _banner;
    private bool _remote, _hostForeground, _parked, _testMode, _panelOpen;
    private OverlayView _view;

    public OverlayController(Func<AppSettings> settings, DispatcherQueue dispatcher)
    {
        _settings = settings;
        _bannerTimer = dispatcher.CreateTimer();
        _bannerTimer.IsRepeating = false;
        _bannerTimer.Tick += (_, _) => DismissBanner();
        _reassertTimer = dispatcher.CreateTimer();
        _reassertTimer.Interval = ReassertInterval;
        _reassertTimer.Tick += (_, _) => _window.Reassert();
        _window.Clicked += OnClicked;
    }

    /// <summary>"Abrir" on a banner: go to the app that sent the event.</summary>
    public event EventHandler<HostEvent>? OpenEventRequested;

    /// <summary>Ticker or quick access: show the ScreenPivot panel on top of the session.</summary>
    public event EventHandler? PanelRequested;

    public event EventHandler? ReturnRequested;

    public void Update(bool remote, bool hostForeground, bool parked, bool panelOpen, DetectionResult detection, IReadOnlyList<HostEvent> pending)
    {
        _remote = remote;
        _hostForeground = hostForeground;
        _parked = parked;
        _panelOpen = panelOpen;
        _detection = detection;
        _pending = pending;
        if (!remote && !_testMode)
        {
            _banner = null;
            _bannerTimer.Stop();
        }
        Render();
    }

    public void Notify(HostEvent e, IReadOnlyList<HostEvent> pending)
    {
        _pending = pending;
        if (e.Priority >= Priority.High && (_banner is null || e.Priority >= _banner.Priority))
        {
            _banner = e;
            _testMode = false;
            _bannerTimer.Stop();
            if (e.Priority < Priority.Critical)
            {
                _bannerTimer.Interval = TimeSpan.FromSeconds(Math.Max(2, _settings().HighBannerSeconds));
                _bannerTimer.Start();
            }
        }
        Render();
    }

    public void SetPending(IReadOnlyList<HostEvent> pending)
    {
        _pending = pending;
        Render();
    }

    /// <summary>Shows a sample critical banner even outside remote mode, for checking placement and z-order.</summary>
    public void ShowTest(HostEvent sample)
    {
        _testMode = true;
        _banner = sample;
        _bannerTimer.Stop();
        _bannerTimer.Interval = TimeSpan.FromSeconds(15);
        _bannerTimer.Start();
        Render();
    }

    public void Reassert() => _window.Reassert();

    private void DismissBanner()
    {
        _banner = null;
        _testMode = false;
        _bannerTimer.Stop();
        Render();
    }

    private void OnClicked(object? sender, OverlayClick click)
    {
        DiagnosticsLog.Write($"overlay click view={_view} close={click.Close}");
        switch (_view)
        {
            case OverlayView.Banner:
                var e = _banner;
                bool test = _testMode;
                DismissBanner();
                if (!click.Close && e is not null && !test) OpenEventRequested?.Invoke(this, e);
                break;
            case OverlayView.Ticker:
            case OverlayView.QuickAccess:
                PanelRequested?.Invoke(this, EventArgs.Empty);
                break;
            case OverlayView.ReturnToRemote:
                ReturnRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void Render()
    {
        var settings = _settings();
        _view = OverlayPlanner.Decide(new OverlayInputs(
            _remote, _hostForeground, _parked, _banner is not null, _testMode && _banner is not null,
            _pending.Count, settings.Paused, settings.ShowQuickAccess, _panelOpen));

        var monitor = PickMonitor(settings);
        switch (_view)
        {
            case OverlayView.Banner:
                _window.ShowBanner(_banner!, monitor, settings.Corner);
                break;
            case OverlayView.Ticker:
                _window.ShowPill($"ScreenPivot · {RulesSummarizer.Ticker(_pending)}", _pending.Max(e => e.Priority), 320, monitor, settings.Corner);
                break;
            case OverlayView.QuickAccess:
                _window.ShowPill(Loc.Get("Overlay_QuickAccess"), Priority.Low, 230, monitor, settings.Corner);
                break;
            case OverlayView.ReturnToRemote:
                var text = _pending.Count == 0 || settings.Paused
                    ? Loc.Get("Overlay_Return")
                    : Loc.Format("Overlay_ReturnWithCount", RulesSummarizer.Ticker(_pending));
                _window.ShowPill(text, Priority.Normal, _pending.Count == 0 ? 250 : 400, monitor, settings.Corner);
                break;
            default:
                _window.Hide();
                _reassertTimer.Stop();
                return;
        }
        _window.Reassert();
        _reassertTimer.Start();
    }

    private Rect PickMonitor(AppSettings settings)
    {
        var monitors = _detection.Monitors.Count > 0 ? _detection.Monitors : RemoteSessionDetector.GetMonitors();
        return MonitorPicker.Pick(monitors, _detection.Coverage, settings.MonitorIndex)!.Bounds;
    }

    public void Dispose()
    {
        _bannerTimer.Stop();
        _reassertTimer.Stop();
        _window.Dispose();
    }
}
