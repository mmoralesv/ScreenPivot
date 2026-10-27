using System.Diagnostics;
using ScreenPivot.Core.Detection;
using ScreenPivot.Core.Interop;

namespace ScreenPivot.Core.Switching;

public enum SwitchResult
{
    /// <summary>An existing window of the app is now in the foreground.</summary>
    Focused,
    /// <summary>No window was found, so the app was launched (it activates itself).</summary>
    Launched,
    /// <summary>The window exists but Windows refused to give it the foreground.</summary>
    FocusDenied,
    NotFound,
}

/// <summary>
/// Moves the user between the remote session and host apps. SetForegroundWindow only succeeds when this process
/// received the last input (a click on the overlay, the tray or our window), which is always how these are called.
/// </summary>
public static class WindowSwitcher
{
    /// <summary>The RDP session window, minimized or not; Zero when there is no session.</summary>
    public static IntPtr FindSessionWindow() =>
        RemoteSessionDetector.GetRemoteWindows()
            .FirstOrDefault(w => w.ClassName == RemoteSessionDetector.SessionWindowClass)?.Hwnd ?? IntPtr.Zero;

    public static bool IsAlive(IntPtr hwnd) => hwnd != IntPtr.Zero && WindowInterop.IsWindow(hwnd);

    public static void Minimize(IntPtr hwnd) => WindowInterop.ShowWindow(hwnd, WindowInterop.SW_MINIMIZE);

    public static bool Focus(IntPtr hwnd)
    {
        if (!IsAlive(hwnd)) return false;
        if (Win32.IsIconic(hwnd)) WindowInterop.ShowWindow(hwnd, WindowInterop.SW_RESTORE);
        if (WindowInterop.SetForegroundWindow(hwnd) && Win32.GetForegroundWindow() == hwnd) return true;
        WindowInterop.SwitchToThisWindow(hwnd, true);
        return Win32.GetForegroundWindow() == hwnd;
    }

    /// <summary>
    /// For our own windows when another app (typically the full-screen RDP client) holds the foreground and Windows
    /// refuses a plain SetForegroundWindow: temporarily share input state with the foreground thread, which lets
    /// the call through. Only used right after the user clicked one of our windows.
    /// </summary>
    public static bool ForceForeground(IntPtr hwnd)
    {
        if (Focus(hwnd)) return true;
        var foreground = Win32.GetForegroundWindow();
        uint foregroundThread = Win32.GetWindowThreadProcessId(foreground, out _);
        uint current = WindowInterop.GetCurrentThreadId();
        if (foregroundThread == 0 || foregroundThread == current) return false;
        WindowInterop.AttachThreadInput(current, foregroundThread, true);
        try
        {
            WindowInterop.BringWindowToTop(hwnd);
            WindowInterop.SetForegroundWindow(hwnd);
        }
        finally
        {
            WindowInterop.AttachThreadInput(current, foregroundThread, false);
        }
        return Win32.GetForegroundWindow() == hwnd;
    }

    /// <summary>Top-level, user-facing windows in z-order, excluding remote-client and our own windows.</summary>
    public static IReadOnlyList<WindowCandidate> GetCandidates()
    {
        var ownPid = (uint)Environment.ProcessId;
        var list = new List<WindowCandidate>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.IsCloaked(hwnd)) return true;
            if (WindowInterop.GetWindow(hwnd, WindowInterop.GW_OWNER) != IntPtr.Zero) return true;
            if ((Win32.GetWindowLongPtr(hwnd, WindowInterop.GWL_EXSTYLE).ToInt64() & WindowInterop.WS_EX_TOOLWINDOW) != 0) return true;
            var title = Win32.WindowText(hwnd);
            if (title.Length == 0) return true;
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid) return true;
            var process = RemoteSessionDetector.ProcessName(pid);
            if (RemoteSessionDetector.RemoteProcessNames.Contains(process)) return true;
            list.Add(new WindowCandidate(hwnd, WindowInterop.WindowAppId(hwnd), WindowInterop.ProcessAppId(pid), process, title));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// Brings the exact source window to the front when still open, else the app's most recent window,
    /// else launches the app by its AppUserModelID.
    /// </summary>
    public static SwitchResult FocusApp(string appId, string displayName, long window = 0)
    {
        if (window != 0 && IsAlive((IntPtr)window))
            return Focus((IntPtr)window) ? SwitchResult.Focused : SwitchResult.FocusDenied;

        var match = AppWindowMatcher.Best(GetCandidates(), appId, displayName);
        if (match is not null)
            return Focus(match.Hwnd) ? SwitchResult.Focused : SwitchResult.FocusDenied;

        if (appId.Length == 0) return SwitchResult.NotFound;
        try
        {
            Process.Start(new ProcessStartInfo($@"shell:AppsFolder\{appId}") { UseShellExecute = true });
            return SwitchResult.Launched;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return SwitchResult.NotFound;
        }
    }
}
