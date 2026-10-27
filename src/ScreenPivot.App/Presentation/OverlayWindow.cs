using ScreenPivot.App.Interop;
using ScreenPivot.Core.Detection;
using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;

namespace ScreenPivot.App.Presentation;

internal enum OverlayMode
{
    Hidden,
    /// <summary>Single line: ticker, quick access or "back to the Cloud PC".</summary>
    Pill,
    /// <summary>Two lines plus "Abrir" and "✕" zones on the right.</summary>
    Banner,
}

/// <param name="Close">The click landed on the banner's ✕ zone.</param>
internal readonly record struct OverlayClick(OverlayMode Mode, bool Close);

/// <summary>
/// Plain Win32 topmost window (validated in Phase 0): never activates, so clicking it does not take keyboard
/// focus away from the remote session. Drawn with GDI; one instance, switched between layouts.
/// Must be used on the UI thread.
/// </summary>
internal sealed class OverlayWindow : IDisposable
{
    private const string ClassName = "ScreenPivotOverlay";
    private const int PillHeight = 36;
    private const int BannerWidth = 500, BannerHeight = 76;
    private const int OpenZone = 76, CloseZone = 44;
    private const int SideMargin = 16, TopMargin = 16, BottomMargin = 64; // bottom clears the remote taskbar
    private const int AccentWidth = 6, Padding = 14;

    private static readonly uint Background = NativeUi.Rgb(32, 32, 40);
    private static readonly uint Foreground = NativeUi.Rgb(242, 242, 246);
    private static readonly uint Secondary = NativeUi.Rgb(196, 196, 206);
    private static readonly uint Divider = NativeUi.Rgb(70, 70, 82);

    private readonly WndProc _wndProc;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _backgroundBrush, _dividerBrush;
    private IntPtr _titleFont, _bodyFont;
    private double _scale;
    private string _line1 = "", _line2 = "";
    private Priority _accent = Priority.Normal;

    public OverlayWindow()
    {
        _wndProc = WindowProc;
        _backgroundBrush = NativeUi.CreateSolidBrush(Background);
        _dividerBrush = NativeUi.CreateSolidBrush(Divider);
        var instance = NativeUi.GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = instance,
            lpszClassName = ClassName,
        };
        NativeUi.RegisterClassEx(ref wc);

        const uint exStyle = NativeUi.WS_EX_TOPMOST | NativeUi.WS_EX_TOOLWINDOW | NativeUi.WS_EX_NOACTIVATE | NativeUi.WS_EX_LAYERED;
        _hwnd = NativeUi.CreateWindowEx(exStyle, ClassName, "ScreenPivot", NativeUi.WS_POPUP, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        NativeUi.SetLayeredWindowAttributes(_hwnd, 0, 242, NativeUi.LWA_ALPHA);
    }

    public event EventHandler<OverlayClick>? Clicked;

    public OverlayMode Mode { get; private set; }

    public void ShowPill(string text, Priority accent, int baseWidth, Rect monitor, OverlayCorner corner)
    {
        _line1 = text;
        _line2 = "";
        _accent = accent;
        Show(OverlayMode.Pill, baseWidth, PillHeight, monitor, corner);
    }

    public void ShowBanner(HostEvent e, Rect monitor, OverlayCorner corner)
    {
        _line1 = string.IsNullOrEmpty(e.Title) ? e.App : $"{e.App} · {e.Title}";
        _line2 = FirstLine(e.Body);
        if (_line2.Length == 0) _line2 = e.Reason;
        _accent = e.Priority;
        Show(OverlayMode.Banner, BannerWidth, BannerHeight, monitor, corner);
    }

    public void Hide()
    {
        Mode = OverlayMode.Hidden;
        NativeUi.ShowWindow(_hwnd, NativeUi.SW_HIDE);
    }

    /// <summary>Moves back to the top of the topmost band in case another topmost window covered it.</summary>
    public void Reassert()
    {
        if (Mode == OverlayMode.Hidden) return;
        NativeUi.SetWindowPos(_hwnd, NativeUi.HWND_TOPMOST, 0, 0, 0, 0, NativeUi.SWP_NOMOVE | NativeUi.SWP_NOSIZE | NativeUi.SWP_NOACTIVATE);
    }

    private void Show(OverlayMode mode, int baseWidth, int baseHeight, Rect monitor, OverlayCorner corner)
    {
        double scale = NativeUi.ScaleAt(monitor.Left + monitor.Width / 2, monitor.Top + monitor.Height / 2);
        EnsureFonts(scale);

        int w = (int)(baseWidth * scale), h = (int)(baseHeight * scale);
        int side = (int)(SideMargin * scale);
        bool left = corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft;
        bool top = corner is OverlayCorner.TopLeft or OverlayCorner.TopRight;
        int x = left ? monitor.Left + side : monitor.Right - w - side;
        int y = top ? monitor.Top + (int)(TopMargin * scale) : monitor.Bottom - h - (int)(BottomMargin * scale);

        Mode = mode;
        NativeUi.SetWindowPos(_hwnd, NativeUi.HWND_TOPMOST, x, y, w, h, NativeUi.SWP_NOACTIVATE | NativeUi.SWP_SHOWWINDOW);
        NativeUi.InvalidateRect(_hwnd, IntPtr.Zero, true);
    }

    private void EnsureFonts(double scale)
    {
        if (Math.Abs(scale - _scale) < 0.01) return;
        if (_titleFont != IntPtr.Zero) NativeUi.DeleteObject(_titleFont);
        if (_bodyFont != IntPtr.Zero) NativeUi.DeleteObject(_bodyFont);
        _titleFont = NativeUi.CreateFont(-(int)(14 * scale), 0, 0, 0, NativeUi.FW_SEMIBOLD, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _bodyFont = NativeUi.CreateFont(-(int)(13 * scale), 0, 0, 0, NativeUi.FW_NORMAL, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _scale = scale;
    }

    private int Px(int dip) => (int)(dip * _scale);

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeUi.WM_MOUSEACTIVATE:
                return new IntPtr(NativeUi.MA_NOACTIVATE);
            case NativeUi.WM_SETCURSOR:
                NativeUi.SetCursor(NativeUi.LoadCursor(IntPtr.Zero, NativeUi.IDC_HAND));
                return new IntPtr(1);
            case NativeUi.WM_LBUTTONUP:
                NativeUi.GetClientRect(hwnd, out var rc);
                int x = (short)NativeUi.LowWord(lParam);
                Clicked?.Invoke(this, new OverlayClick(Mode, Mode == OverlayMode.Banner && x >= rc.Right - Px(CloseZone)));
                return IntPtr.Zero;
            case NativeUi.WM_PAINT:
                Paint(hwnd);
                return IntPtr.Zero;
        }
        return NativeUi.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Paint(IntPtr hwnd)
    {
        var hdc = NativeUi.BeginPaint(hwnd, out var ps);
        NativeUi.GetClientRect(hwnd, out var rc);
        NativeUi.FillRect(hdc, ref rc, _backgroundBrush);

        var accentBrush = NativeUi.CreateSolidBrush(AccentColor(_accent));
        var bar = rc with { Right = rc.Left + Px(AccentWidth) };
        NativeUi.FillRect(hdc, ref bar, accentBrush);
        NativeUi.DeleteObject(accentBrush);

        NativeUi.SetBkMode(hdc, NativeUi.TRANSPARENT);
        int pad = Px(Padding);
        const uint format = NativeUi.DT_LEFT | NativeUi.DT_SINGLELINE | NativeUi.DT_VCENTER | NativeUi.DT_END_ELLIPSIS | NativeUi.DT_NOPREFIX;
        const uint centered = NativeUi.DT_CENTER | NativeUi.DT_SINGLELINE | NativeUi.DT_VCENTER | NativeUi.DT_NOPREFIX;
        var oldFont = NativeUi.SelectObject(hdc, _titleFont);

        if (Mode != OverlayMode.Banner)
        {
            var text = rc with { Left = bar.Right + pad, Right = rc.Right - pad };
            NativeUi.SetTextColor(hdc, Foreground);
            NativeUi.DrawText(hdc, _line1, -1, ref text, format);
        }
        else
        {
            var close = rc with { Left = rc.Right - Px(CloseZone) };
            var open = rc with { Left = close.Left - Px(OpenZone), Right = close.Left };
            var text = rc with { Left = bar.Right + pad, Right = open.Left - pad };

            int mid = rc.Top + (rc.Bottom - rc.Top) / 2;
            var upper = text with { Top = rc.Top + pad / 2, Bottom = mid };
            var lower = text with { Top = mid, Bottom = rc.Bottom - pad / 2 };
            NativeUi.SetTextColor(hdc, Foreground);
            NativeUi.DrawText(hdc, _line1, -1, ref upper, format);

            NativeUi.SetTextColor(hdc, Foreground);
            NativeUi.DrawText(hdc, "Abrir", -1, ref open, centered);
            NativeUi.DrawText(hdc, "✕", -1, ref close, centered);

            NativeUi.SelectObject(hdc, _bodyFont);
            NativeUi.SetTextColor(hdc, Secondary);
            NativeUi.DrawText(hdc, _line2, -1, ref lower, format);

            int inset = Px(14);
            var line1 = open with { Right = open.Left + Math.Max(1, Px(1)), Top = rc.Top + inset, Bottom = rc.Bottom - inset };
            var line2 = close with { Right = close.Left + Math.Max(1, Px(1)), Top = rc.Top + inset, Bottom = rc.Bottom - inset };
            NativeUi.FillRect(hdc, ref line1, _dividerBrush);
            NativeUi.FillRect(hdc, ref line2, _dividerBrush);
        }

        NativeUi.SelectObject(hdc, oldFont);
        NativeUi.EndPaint(hwnd, ref ps);
    }

    public static uint AccentColor(Priority p) => p switch
    {
        Priority.Critical => NativeUi.Rgb(232, 17, 35),
        Priority.High => NativeUi.Rgb(247, 99, 12),
        Priority.Normal => NativeUi.Rgb(0, 120, 212),
        _ => NativeUi.Rgb(120, 120, 130),
    };

    private static string FirstLine(string text)
    {
        var i = text.IndexOf('\n');
        return (i < 0 ? text : text[..i]).Trim();
    }

    public void Dispose()
    {
        NativeUi.DestroyWindow(_hwnd);
        NativeUi.DeleteObject(_backgroundBrush);
        NativeUi.DeleteObject(_dividerBrush);
        if (_titleFont != IntPtr.Zero) NativeUi.DeleteObject(_titleFont);
        if (_bodyFont != IntPtr.Zero) NativeUi.DeleteObject(_bodyFont);
        GC.KeepAlive(_wndProc);
    }
}
