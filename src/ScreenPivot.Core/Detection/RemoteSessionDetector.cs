using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenPivot.Core.Interop;

namespace ScreenPivot.Core.Detection;

public sealed record MonitorDesc(int Index, string Device, Rect Bounds, Rect WorkArea, bool Primary);

public sealed record RemoteWindow(IntPtr Hwnd, string Process, string Title, string ClassName, Rect Bounds, bool Iconic);

public sealed record DetectionResult(
    IReadOnlyList<MonitorDesc> Monitors,
    IReadOnlyList<RemoteWindow> Windows,
    IReadOnlyList<CoverageKind> Coverage,
    bool ForegroundIsRemote)
{
    public static readonly DetectionResult Empty = new(Array.Empty<MonitorDesc>(), Array.Empty<RemoteWindow>(), Array.Empty<CoverageKind>(), false);

    public bool IsRemote(bool includeMaximized) =>
        Coverage.Any(c => c == CoverageKind.FullScreen || (includeMaximized && c == CoverageKind.Maximized));

    /// <summary>Identity of the coverage state, ignoring window titles and foreground changes.</summary>
    public string CoverageKey => string.Join(",", Coverage);

    public string SessionName => Windows.FirstOrDefault(w => w.ClassName == RemoteSessionDetector.SessionWindowClass)?.Title ?? "";
}

public static class RemoteSessionDetector
{
    /// <summary>Window class of an RDP session window (msrdc.exe from Windows App and mstsc.exe).</summary>
    public const string SessionWindowClass = "TscShellContainerClass";

    /// <summary>Windows App (Windows365.exe launches msrdc.exe), classic RDP client and Remote Desktop client.</summary>
    public static readonly HashSet<string> RemoteProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "msrdc", "msrdcw", "mstsc", "Windows365",
    };

    private static readonly Dictionary<uint, string> ProcessNameCache = new();

    public static IReadOnlyList<MonitorDesc> GetMonitors()
    {
        var monitors = new List<MonitorDesc>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (Win32.GetMonitorInfo(hMon, ref info))
                monitors.Add(new MonitorDesc(monitors.Count, info.szDevice, info.rcMonitor.ToRect(), info.rcWork.ToRect(),
                    (info.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    public static string ProcessName(uint pid)
    {
        lock (ProcessNameCache)
        {
            if (ProcessNameCache.TryGetValue(pid, out var name)) return name;
            try { name = Process.GetProcessById((int)pid).ProcessName; }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { name = ""; }
            ProcessNameCache[pid] = name;
            return name;
        }
    }

    public static bool IsRemoteWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        return RemoteProcessNames.Contains(ProcessName(pid));
    }

    public static IReadOnlyList<RemoteWindow> GetRemoteWindows()
    {
        var windows = new List<RemoteWindow>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.IsCloaked(hwnd)) return true;
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            var process = ProcessName(pid);
            if (!RemoteProcessNames.Contains(process)) return true;
            var bounds = Win32.VisibleBounds(hwnd);
            if (bounds.Area == 0) return true;
            windows.Add(new RemoteWindow(hwnd, process, Win32.WindowText(hwnd), Win32.ClassName(hwnd), bounds, Win32.IsIconic(hwnd)));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public static DetectionResult Detect()
    {
        // PIDs get reused; the cache only needs to live between detections (at most the 5 s safety interval).
        lock (ProcessNameCache) ProcessNameCache.Clear();
        var monitors = GetMonitors();
        var windows = GetRemoteWindows();
        var visible = windows.Where(w => !w.Iconic).Select(w => w.Bounds).ToList();
        var coverage = monitors.Select(m => MonitorCoverage.Classify(m.Bounds, m.WorkArea, visible)).ToList();
        return new DetectionResult(monitors, windows, coverage, IsRemoteWindow(Win32.GetForegroundWindow()));
    }
}
