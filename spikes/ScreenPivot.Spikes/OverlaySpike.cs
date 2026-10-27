using System.Diagnostics;

namespace ScreenPivot.Spikes;

/// <summary>
/// Click-through, non-activating topmost banner. Measures whether it stays above the Windows App window,
/// whether it ever takes the foreground, and how often the z-order must be re-asserted.
/// </summary>
public static class OverlaySpike
{
    private const string ClassName = "ScreenPivotSpikeOverlay";
    private const int BaseWidth = 560, BaseHeight = 44, BaseMargin = 8;

    private static WndProc? _wndProc; // kept alive for the native side
    private static string _text = "";
    private static IntPtr _font, _background;

    private sealed class Stats
    {
        public int Ticks, RemoteForegroundTicks, AboveWhileRemoteForeground, FoundBelow, Reasserts, StoleForeground, ForegroundEvents;
    }

    public static void Run(int seconds, int delay, int? monitorIndex, bool reassert)
    {
        if (delay > 0)
        {
            Console.WriteLine($"El overlay aparecerá en {delay}s: cambia ahora al Cloud PC a pantalla completa.");
            Thread.Sleep(delay * 1000);
        }

        var detection = RemoteSessionDetector.Detect();
        var monitor = PickMonitor(detection, monitorIndex);
        Console.WriteLine($"Monitor elegido: M{monitor.Index} {monitor.Bounds} (cobertura remota: {detection.Coverage[monitor.Index]})");

        var hwnd = CreateOverlay(monitor);
        var stats = new Stats();
        string lastLine = "";

        WinEventProc onForeground = (_, _, fg, _, _, _, _) =>
        {
            stats.ForegroundEvents++;
            if (fg == hwnd) stats.StoleForeground++;
            if (reassert) { Reassert(hwnd); stats.Reasserts++; }
        };
        var hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, onForeground, 0, 0,
            Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);

        var sw = Stopwatch.StartNew();
        MessageLoop.Run(1000, seconds, () =>
        {
            stats.Ticks++;
            var fg = Native.GetForegroundWindow();
            if (fg == hwnd) stats.StoleForeground++;

            var coveringRemote = CoveringRemoteWindows();
            bool above = IsAbove(hwnd, coveringRemote);
            if (!above && coveringRemote.Count > 0)
            {
                stats.FoundBelow++;
                if (reassert) { Reassert(hwnd); stats.Reasserts++; above = IsAbove(hwnd, coveringRemote); }
            }

            bool remoteFg = RemoteSessionDetector.IsRemoteWindow(fg) && coveringRemote.Count > 0;
            if (remoteFg)
            {
                stats.RemoteForegroundTicks++;
                if (above) stats.AboveWhileRemoteForeground++;
            }

            _text = $"ScreenPivot spike · {sw.Elapsed:mm\\:ss} · " +
                    (coveringRemote.Count == 0 ? "sin Cloud PC a pantalla completa" : above ? "encima del Cloud PC ✓" : "DEBAJO del Cloud PC ✗");
            Native.InvalidateRect(hwnd, IntPtr.Zero, true);

            var line = $"foreground={RemoteSessionDetector.ProcessNameOf(fg)} remotoCubriendo={coveringRemote.Count} overlayEncima={above}";
            if (line != lastLine)
            {
                Console.WriteLine($"[{sw.Elapsed:mm\\:ss}] {line}");
                lastLine = line;
            }
        });

        Native.UnhookWinEvent(hook);
        GC.KeepAlive(onForeground);
        Native.DestroyWindow(hwnd);
        Native.DeleteObject(_font);
        Native.DeleteObject(_background);

        Console.WriteLine();
        Console.WriteLine("Resultado:");
        Console.WriteLine($"  ticks={stats.Ticks}, eventos foreground={stats.ForegroundEvents}");
        Console.WriteLine($"  segundos con Cloud PC en foreground cubriendo: {stats.RemoteForegroundTicks}, con overlay encima: {stats.AboveWhileRemoteForeground}");
        Console.WriteLine($"  veces encontrado debajo: {stats.FoundBelow}, re-afirmaciones topmost: {stats.Reasserts} (reassert={(reassert ? "on" : "off")})");
        Console.WriteLine($"  overlay tomó el foreground: {stats.StoleForeground} (esperado 0)");
        Console.WriteLine(stats.RemoteForegroundTicks == 0
            ? "  => NO CONCLUYENTE: el Cloud PC nunca estuvo en foreground a pantalla completa durante la prueba."
            : stats.AboveWhileRemoteForeground == stats.RemoteForegroundTicks && stats.StoleForeground == 0
                ? "  => OK: el overlay se mantuvo visible sobre el Cloud PC sin robar el foco."
                : "  => FALLO PARCIAL: revisar los conteos anteriores.");
    }

    private static MonitorDesc PickMonitor(DetectionResult d, int? index)
    {
        if (index is int i && i >= 0 && i < d.Monitors.Count) return d.Monitors[i];
        var covered = d.Monitors.FirstOrDefault(m => d.Coverage[m.Index] != CoverageKind.None);
        return covered ?? d.Monitors.First(m => m.Primary);
    }

    private static IntPtr CreateOverlay(MonitorDesc monitor)
    {
        var hInstance = Native.GetModuleHandle(null);
        _wndProc = WindowProc;
        _background = Native.CreateSolidBrush(Native.Rgb(28, 28, 36));
        var wc = new WNDCLASSEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            hbrBackground = _background,
            lpszClassName = ClassName,
        };
        Native.RegisterClassEx(ref wc);

        const uint exStyle = Native.WS_EX_TOPMOST_U | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE |
                             Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
        var hwnd = Native.CreateWindowEx(exStyle, ClassName, "ScreenPivot overlay", Native.WS_POPUP,
            monitor.Bounds.Left, monitor.Bounds.Top, BaseWidth, BaseHeight, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        Native.SetLayeredWindowAttributes(hwnd, 0, 235, Native.LWA_ALPHA);

        // Size for the DPI of the monitor the window now lives on.
        double scale = Native.GetDpiForWindow(hwnd) / 96.0;
        int w = (int)(BaseWidth * scale), h = (int)(BaseHeight * scale);
        int x = monitor.Bounds.Left + (monitor.Bounds.Width - w) / 2;
        int y = monitor.Bounds.Top + (int)(BaseMargin * scale);
        _font = Native.CreateFont(-(int)(15 * scale), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");

        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
        Console.WriteLine($"Overlay creado 0x{hwnd:X} en ({x},{y}) {w}x{h}, escala {scale:0.##}");
        return hwnd;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Native.WM_NCHITTEST:
                return new IntPtr(Native.HTTRANSPARENT);
            case Native.WM_MOUSEACTIVATE:
                return new IntPtr(Native.MA_NOACTIVATE);
            case Native.WM_PAINT:
                var hdc = Native.BeginPaint(hwnd, out var ps);
                Native.GetClientRect(hwnd, out var rc);
                Native.FillRect(hdc, ref rc, _background);
                var old = Native.SelectObject(hdc, _font);
                Native.SetBkMode(hdc, Native.TRANSPARENT);
                Native.SetTextColor(hdc, Native.Rgb(240, 240, 245));
                Native.DrawText(hdc, _text, -1, ref rc, Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_END_ELLIPSIS);
                Native.SelectObject(hdc, old);
                Native.EndPaint(hwnd, ref ps);
                return IntPtr.Zero;
        }
        return Native.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static void Reassert(IntPtr hwnd) =>
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    private static HashSet<IntPtr> CoveringRemoteWindows()
    {
        var monitors = RemoteSessionDetector.GetMonitors();
        return RemoteSessionDetector.GetRemoteWindows()
            .Where(w => !w.Iconic && monitors.Any(m => MonitorCoverage.Classify(m.Bounds, m.WorkArea, w.Bounds) != CoverageKind.None))
            .Select(w => w.Hwnd)
            .ToHashSet();
    }

    /// <summary>True when the overlay precedes every covering remote window in the global z-order.</summary>
    private static bool IsAbove(IntPtr overlay, HashSet<IntPtr> remote)
    {
        if (remote.Count == 0) return true;
        for (var h = Native.GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = Native.GetWindow(h, Native.GW_HWNDNEXT))
        {
            if (h == overlay) return true;
            if (remote.Contains(h)) return false;
        }
        return false;
    }
}
