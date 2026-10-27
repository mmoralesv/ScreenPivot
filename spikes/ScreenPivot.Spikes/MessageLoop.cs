using System.Diagnostics;

namespace ScreenPivot.Spikes;

internal static class MessageLoop
{
    /// <summary>Pumps Win32 messages on the current thread, calling <paramref name="tick"/> every <paramref name="tickMs"/> until the deadline.</summary>
    public static void Run(uint tickMs, int seconds, Action tick)
    {
        var timer = Native.SetTimer(IntPtr.Zero, IntPtr.Zero, tickMs, IntPtr.Zero);
        var sw = Stopwatch.StartNew();
        while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == Native.WM_TIMER && msg.hwnd == IntPtr.Zero && msg.wParam == timer)
            {
                tick();
                if (sw.Elapsed.TotalSeconds >= seconds) break;
                continue;
            }
            Native.TranslateMessage(ref msg);
            Native.DispatchMessage(ref msg);
        }
        Native.KillTimer(IntPtr.Zero, timer);
    }
}
