using System.Diagnostics;

namespace ScreenPivot.Spikes;

public sealed record MonitorDesc(int Index, string Device, Rect Bounds, Rect WorkArea, bool Primary);

public sealed record RemoteWindow(IntPtr Hwnd, string Process, uint Pid, string Title, string ClassName, Rect Bounds, bool Topmost, bool Iconic);

public sealed record DetectionResult(
    IReadOnlyList<MonitorDesc> Monitors,
    IReadOnlyList<RemoteWindow> Windows,
    IReadOnlyList<CoverageKind> Coverage,
    string NotificationState,
    string ForegroundProcess,
    bool ForegroundIsRemote)
{
    public bool IsCovering => Coverage.Any(c => c != CoverageKind.None);

    /// <summary>One-line summary used to detect state changes in watch mode.</summary>
    public string Summary =>
        $"cubiertos=[{string.Join(", ", Coverage.Select((c, i) => $"M{i}:{c}"))}] " +
        $"foreground={ForegroundProcess}{(ForegroundIsRemote ? " (remoto)" : "")} quns={NotificationState}";
}

public static class RemoteSessionDetector
{
    /// <summary>Windows App (Windows365.exe hosts msrdc.exe), classic RDP client and Remote Desktop client.</summary>
    public static readonly HashSet<string> RemoteProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "msrdc", "msrdcw", "mstsc", "Windows365",
    };

    private static readonly Dictionary<uint, string> ProcessNameCache = new();

    public static IReadOnlyList<MonitorDesc> GetMonitors()
    {
        var monitors = new List<MonitorDesc>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (Native.GetMonitorInfo(hMon, ref info))
                monitors.Add(new MonitorDesc(monitors.Count, info.szDevice, info.rcMonitor.ToRect(), info.rcWork.ToRect(),
                    (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    public static string ProcessName(uint pid)
    {
        if (ProcessNameCache.TryGetValue(pid, out var name)) return name;
        try { name = Process.GetProcessById((int)pid).ProcessName; }
        catch { name = $"pid:{pid}"; }
        ProcessNameCache[pid] = name;
        return name;
    }

    public static string ProcessNameOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(ninguno)";
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        return ProcessName(pid);
    }

    public static bool IsRemoteWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && RemoteProcessNames.Contains(ProcessNameOf(hwnd));

    public static IReadOnlyList<RemoteWindow> GetRemoteWindows()
    {
        var windows = new List<RemoteWindow>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsCloaked(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            var process = ProcessName(pid);
            if (!RemoteProcessNames.Contains(process)) return true;
            var bounds = Native.VisibleBounds(hwnd);
            if (bounds.Area == 0) return true;
            windows.Add(new RemoteWindow(hwnd, process, pid, Native.WindowText(hwnd), Native.ClassName(hwnd), bounds,
                Native.IsTopmost(hwnd), Native.IsIconic(hwnd)));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public static DetectionResult Detect()
    {
        var monitors = GetMonitors();
        var windows = GetRemoteWindows();
        var visible = windows.Where(w => !w.Iconic).Select(w => w.Bounds).ToList();
        var coverage = monitors.Select(m => MonitorCoverage.Classify(m.Bounds, m.WorkArea, visible)).ToList();
        var fg = Native.GetForegroundWindow();
        return new DetectionResult(monitors, windows, coverage, Native.NotificationState(), ProcessNameOf(fg), IsRemoteWindow(fg));
    }

    public static void Print(DetectionResult r)
    {
        Console.WriteLine($"Monitores ({r.Monitors.Count}):");
        foreach (var m in r.Monitors)
            Console.WriteLine($"  M{m.Index} {m.Device}{(m.Primary ? " [principal]" : "")} bounds={m.Bounds} work={m.WorkArea} -> {r.Coverage[m.Index]}");
        Console.WriteLine($"Ventanas de cliente remoto ({r.Windows.Count}):");
        foreach (var w in r.Windows)
            Console.WriteLine($"  0x{w.Hwnd:X} {w.Process}({w.Pid}) class='{w.ClassName}' title='{w.Title}' bounds={w.Bounds}" +
                              $"{(w.Topmost ? " TOPMOST" : "")}{(w.Iconic ? " MINIMIZADA" : "")}");
        Console.WriteLine($"Foreground: {r.ForegroundProcess}{(r.ForegroundIsRemote ? " (remoto)" : "")}");
        Console.WriteLine($"SHQueryUserNotificationState: {r.NotificationState}");
        Console.WriteLine($"=> Sesión remota cubriendo pantalla: {(r.IsCovering ? "SÍ" : "NO")}");
    }

    /// <summary>
    /// Watches with WinEvent hooks (foreground, minimize, location changes) and re-evaluates only when something fired,
    /// plus a slow safety re-check. Prints every state transition.
    /// </summary>
    public static void Watch(int seconds)
    {
        int events = 0;
        bool dirty = true;
        string last = "";
        WinEventProc callback = (_, evt, hwnd, idObject, _, _, _) =>
        {
            if (evt == Native.EVENT_OBJECT_LOCATIONCHANGE && (idObject != Native.OBJID_WINDOW || !IsRemoteWindow(hwnd))) return;
            events++;
            dirty = true;
        };
        const uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
        var hooks = new[]
        {
            Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, callback, 0, 0, flags),
            Native.SetWinEventHook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, callback, 0, 0, flags),
            Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, callback, 0, 0, flags),
        };
        Console.WriteLine($"Observando {seconds}s (hooks: {hooks.Count(h => h != IntPtr.Zero)}/3). Maximiza/minimiza el Cloud PC o cambia de ventana...");

        var sw = Stopwatch.StartNew();
        long lastSafety = 0;
        MessageLoop.Run(250, seconds, () =>
        {
            if (!dirty && sw.ElapsedMilliseconds - lastSafety < 5000) return;
            dirty = false;
            lastSafety = sw.ElapsedMilliseconds;
            var r = Detect();
            if (r.Summary == last) return;
            last = r.Summary;
            Console.WriteLine($"[{sw.Elapsed:mm\\:ss\\.f}] {(r.IsCovering ? "REMOTO" : "host  ")} {r.Summary} (eventos={events})");
        });

        foreach (var h in hooks) Native.UnhookWinEvent(h);
        GC.KeepAlive(callback);
        Console.WriteLine($"Fin. Eventos relevantes recibidos: {events}");
    }
}
