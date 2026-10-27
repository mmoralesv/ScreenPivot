using System.Diagnostics;
using ScreenPivot.Core.Detection;
using ScreenPivot.Core.Interop;
using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;
using ScreenPivot.Core.Switching;

namespace ScreenPivot.Core.Sources;

/// <summary>
/// Signals from apps that do not send notifications: taskbar flashes (shell hook HSHELL_FLASH) and window title
/// changes (EVENT_OBJECT_NAMECHANGE) matched by <see cref="TitleRule"/>s. Ignores the foreground window (the user
/// is already looking at it), remote-client windows and, unless asked, this process. Start it on the UI thread;
/// <see cref="EventReceived"/> fires there.
/// </summary>
public sealed class WindowActivitySource : IDisposable
{
    public static readonly TimeSpan FlashCooldown = TimeSpan.FromSeconds(30);
    private const string ClassName = "ScreenPivotShellHook";

    private readonly Func<AppSettings> _settings;
    private readonly bool _includeOwnProcess;
    private readonly TitleTracker _titles = new();
    private readonly Dictionary<long, DateTimeOffset> _lastFlash = new();
    private static readonly TimeSpan PidNameTtl = TimeSpan.FromSeconds(60);
    private readonly Dictionary<string, string> _appNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, (string Name, DateTimeOffset At)> _pidNames = new();
    private WindowInterop.WndProc? _wndProc;
    private WinEventProc? _hookProc;
    private IntPtr _hwnd, _nameHook, _destroyHook;
    private uint _shellMessage;

    public WindowActivitySource(Func<AppSettings> settings, bool includeOwnProcess = false)
    {
        _settings = settings;
        _includeOwnProcess = includeOwnProcess;
    }

    public event EventHandler<HostEvent>? EventReceived;

    public bool IsRunning => _hwnd != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning) return;

        foreach (var w in WindowSwitcher.GetCandidates())
            _titles.Seed(w.Hwnd, w.Title, _settings().TitleRules, AppName(w.Hwnd));

        _wndProc = WindowProc;
        var instance = WindowInterop.GetModuleHandle(null);
        var wc = new WindowInterop.WNDCLASSEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<WindowInterop.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = instance,
            lpszClassName = ClassName,
        };
        WindowInterop.RegisterClassEx(ref wc);
        // Shell hooks need a real (hidden) top-level window, not a message-only one.
        _hwnd = WindowInterop.CreateWindowEx(0, ClassName, "ScreenPivot shell hook", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        _shellMessage = WindowInterop.RegisterWindowMessage("SHELLHOOK");
        WindowInterop.RegisterShellHookWindow(_hwnd);

        _hookProc = OnWinEvent;
        _nameHook = Win32.SetWinEventHook(WindowInterop.EVENT_OBJECT_NAMECHANGE, WindowInterop.EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _hookProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        _destroyHook = Win32.SetWinEventHook(WindowInterop.EVENT_OBJECT_DESTROY, WindowInterop.EVENT_OBJECT_DESTROY, IntPtr.Zero, _hookProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _shellMessage && _shellMessage != 0)
        {
            if (wParam.ToInt64() == WindowInterop.HSHELL_FLASH) OnFlash(lParam);
            return IntPtr.Zero;
        }
        return WindowInterop.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Win32.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;
        if (evt == WindowInterop.EVENT_OBJECT_DESTROY)
        {
            _titles.Forget(hwnd);
            _lastFlash.Remove(hwnd);
            return;
        }

        var settings = _settings();
        if (!settings.WatchTitles || !IsWatchable(hwnd)) return;

        var app = AppName(hwnd);
        var title = Win32.WindowText(hwnd);
        foreach (var match in _titles.OnTitleChanged(hwnd, title, settings.TitleRules, app))
        {
            Raise(new HostEvent($"title:{hwnd}:{DateTimeOffset.Now.Ticks}", app, match.Rule.Name, title, DateTimeOffset.Now, EventOrigin.TitleChange)
            {
                WindowHandle = hwnd,
                SourcePriority = match.Rule.Priority,
                SourceRule = match.Rule.Name,
            });
        }
    }

    private void OnFlash(IntPtr hwnd)
    {
        if (!_settings().WatchFlashes || !IsWatchable(hwnd)) return;

        var now = DateTimeOffset.Now;
        if (_lastFlash.TryGetValue(hwnd, out var last) && now - last < FlashCooldown) return;
        _lastFlash[hwnd] = now;

        // No body: the explanation is localized when shown (HostEvent.DisplayBody).
        Raise(new HostEvent($"flash:{hwnd}:{now.Ticks}", AppName(hwnd), Win32.WindowText(hwnd), "", now, EventOrigin.WindowFlash)
        {
            WindowHandle = hwnd,
        });
    }

    private bool IsWatchable(IntPtr hwnd)
    {
        if (WindowInterop.GetAncestor(hwnd, WindowInterop.GA_ROOT) != hwnd || !Win32.IsWindowVisible(hwnd)) return false;
        if (hwnd == Win32.GetForegroundWindow()) return false;
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (!_includeOwnProcess && pid == Environment.ProcessId) return false;
        return !RemoteSessionDetector.RemoteProcessNames.Contains(RemoteSessionDetector.ProcessName(pid));
    }

    /// <summary>Friendly name from the executable's file description ("Microsoft Teams"), else the process name.</summary>
    private string AppName(IntPtr hwnd)
    {
        // Title changes are frequent; keep the per-PID lookup short-lived because PIDs get reused.
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        var now = DateTimeOffset.UtcNow;
        if (_pidNames.TryGetValue(pid, out var cached) && now - cached.At < PidNameTtl) return cached.Name;
        var name = ResolveAppName(pid);
        _pidNames[pid] = (name, now);
        return name;
    }

    private string ResolveAppName(uint pid)
    {
        var path = WindowInterop.ProcessImagePath(pid);
        if (path.Length == 0) return RemoteSessionDetector.ProcessName(pid);
        if (_appNames.TryGetValue(path, out var name)) return name;
        // Packaged and protected binaries may refuse version info; fall back to the exe name.
        try { name = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? ""; }
        catch (FileNotFoundException) { name = ""; }
        if (name.Length == 0) name = Path.GetFileNameWithoutExtension(path);
        _appNames[path] = name;
        return name;
    }

    private void Raise(HostEvent e) => EventReceived?.Invoke(this, e);

    public void Dispose()
    {
        if (_nameHook != IntPtr.Zero) Win32.UnhookWinEvent(_nameHook);
        if (_destroyHook != IntPtr.Zero) Win32.UnhookWinEvent(_destroyHook);
        if (_hwnd != IntPtr.Zero)
        {
            WindowInterop.DeregisterShellHookWindow(_hwnd);
            WindowInterop.DestroyWindow(_hwnd);
        }
        _nameHook = _destroyHook = _hwnd = IntPtr.Zero;
        GC.KeepAlive(_wndProc);
        GC.KeepAlive(_hookProc);
    }
}
