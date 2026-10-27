using System.Diagnostics;
using ScreenPivot.Core.Rules;
using ScreenPivot.Core.Sources;

namespace ScreenPivot.Spikes;

/// <summary>
/// Phase 2 checks: <c>activity</c> prints what WindowActivitySource reports (no remote gating);
/// <c>fake-app</c> is a separate process that behaves like an app without notifications.
/// </summary>
public static class ActivitySpike
{
    public static void Watch(int seconds)
    {
        var settings = new AppSettings();
        using var source = new WindowActivitySource(() => settings);
        source.EventReceived += (_, e) =>
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {e.Origin} app='{e.App}' título='{e.Title}' detalle='{e.Body}' regla='{e.SourceRule}' hwnd=0x{e.WindowHandle:X}");
        source.Start();
        Console.WriteLine($"Observando parpadeos y títulos {seconds}s...");
        MessageLoop.Run(500, seconds, () => { });
    }

    /// <summary>Minimized, never activated: changes its title to a build result, then flashes its taskbar button.</summary>
    public static void FakeApp(int delay)
    {
        WndProc proc = (h, m, w, l) => Native.DefWindowProc(h, m, w, l);
        var instance = Native.GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = proc,
            hInstance = instance,
            lpszClassName = "ScreenPivotFakeApp",
        };
        Native.RegisterClassEx(ref wc);
        var hwnd = Native.CreateWindowEx(0, "ScreenPivotFakeApp", "FakeApp - inicio", Native.WS_OVERLAPPEDWINDOW,
            100, 100, 600, 400, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        Native.ShowWindow(hwnd, Native.SW_SHOWMINNOACTIVE);
        Console.WriteLine($"FakeApp 0x{hwnd:X} creada minimizada (pid {Environment.ProcessId}).");

        var steps = new (int AtSeconds, Action Act)[]
        {
            (delay, () => Say(hwnd, "FakeApp - Building...")),
            (delay + 2, () => Say(hwnd, "FakeApp - Build succeeded")),
            (delay + 4, () =>
            {
                var info = new Native.FLASHWINFO
                {
                    cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.FLASHWINFO>(),
                    hwnd = hwnd,
                    dwFlags = Native.FLASHW_ALL | Native.FLASHW_TIMERNOFG,
                };
                Native.FlashWindowEx(ref info);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] FlashWindowEx");
            }),
        };

        var sw = Stopwatch.StartNew();
        int next = 0;
        MessageLoop.Run(250, delay + 10, () =>
        {
            while (next < steps.Length && sw.Elapsed.TotalSeconds >= steps[next].AtSeconds) steps[next++].Act();
        });
        Native.DestroyWindow(hwnd);
        GC.KeepAlive(proc);
    }

    private static void Say(IntPtr hwnd, string title)
    {
        Native.SetWindowText(hwnd, title);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] título -> {title}");
    }
}
