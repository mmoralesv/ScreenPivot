using System.Diagnostics;
using ScreenPivot.Core.Interop;

namespace ScreenPivot.Core.Detection;

/// <summary>
/// Event-driven watcher: WinEvent hooks (foreground, minimize, remote window moves) mark the state dirty and a
/// 250 ms Win32 timer re-detects only then, plus a 5 s safety re-check. Must be started on a thread that pumps
/// Win32 messages (the UI thread); <see cref="Changed"/> and <see cref="ForegroundChanged"/> fire on it.
/// </summary>
public sealed class RemoteSessionMonitor : IDisposable
{
    private const uint TickMs = 250;
    private const long SafetyMs = 5000;

    private readonly List<IntPtr> _hooks = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private WinEventProc? _hookProc;
    private TimerProc? _timerProc;
    private IntPtr _timer;
    private bool _dirty = true;
    private long _lastCheck;

    public DetectionResult Current { get; private set; } = DetectionResult.Empty;

    /// <summary>Coverage of any monitor changed.</summary>
    public event EventHandler<DetectionResult>? Changed;

    /// <summary>Raised on every foreground switch; the overlay uses it to re-assert its z-order.</summary>
    public event EventHandler? ForegroundChanged;

    public void Start()
    {
        _hookProc = OnWinEvent;
        _timerProc = (_, _, _, _) => OnTick();
        const uint flags = Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS;
        // Foreground includes our own windows: activating the ScreenPivot window also means "the user is on the host".
        _hooks.Add(Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hookProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT));
        _hooks.Add(Win32.SetWinEventHook(Win32.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _hookProc, 0, 0, flags));
        _hooks.Add(Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _hookProc, 0, 0, flags));
        _timer = Win32.SetTimer(IntPtr.Zero, IntPtr.Zero, TickMs, _timerProc);
        OnTick();
    }

    /// <summary>Forces a re-check on the next tick (e.g. after settings change).</summary>
    public void Invalidate() => _dirty = true;

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt == Win32.EVENT_OBJECT_LOCATIONCHANGE)
        {
            if (idObject != Win32.OBJID_WINDOW || !RemoteSessionDetector.IsRemoteWindow(hwnd)) return;
        }
        else if (evt == Win32.EVENT_SYSTEM_FOREGROUND)
        {
            ForegroundChanged?.Invoke(this, EventArgs.Empty);
        }
        _dirty = true;
    }

    private void OnTick()
    {
        var now = _clock.ElapsedMilliseconds;
        if (!_dirty && now - _lastCheck < SafetyMs) return;
        _dirty = false;
        _lastCheck = now;

        var previous = Current;
        Current = RemoteSessionDetector.Detect();
        if (Current.CoverageKey != previous.CoverageKey)
            Changed?.Invoke(this, Current);
    }

    public void Dispose()
    {
        foreach (var hook in _hooks.Where(h => h != IntPtr.Zero)) Win32.UnhookWinEvent(hook);
        _hooks.Clear();
        if (_timer != IntPtr.Zero) Win32.KillTimer(IntPtr.Zero, _timer);
        _timer = IntPtr.Zero;
    }
}
