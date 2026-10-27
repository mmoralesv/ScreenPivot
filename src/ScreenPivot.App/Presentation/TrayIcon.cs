using System.Runtime.InteropServices;
using ScreenPivot.App.Interop;

namespace ScreenPivot.App.Presentation;

internal enum TrayCommand
{
    OpenDigest = 1,
    OpenSettings = 2,
    TogglePause = 3,
    Exit = 4,
    ReturnToRemote = 5,
    MinimizeRemote = 6,
}

/// <summary>Notification-area icon backed by a message-only window. Use on the UI thread.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const string ClassName = "ScreenPivotTray";
    private const uint CallbackMessage = NativeUi.WM_APP + 1;

    private readonly WndProc _wndProc;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private readonly uint _taskbarCreated;
    private string _tooltip = "ScreenPivot";

    public TrayIcon(string iconPath)
    {
        _wndProc = WindowProc;
        var instance = NativeUi.GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = instance,
            lpszClassName = ClassName,
        };
        NativeUi.RegisterClassEx(ref wc);
        _hwnd = NativeUi.CreateWindowEx(0, ClassName, "ScreenPivot tray", 0, 0, 0, 0, 0, NativeUi.HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
        _icon = NativeUi.LoadImage(IntPtr.Zero, iconPath, NativeUi.IMAGE_ICON, 0, 0, NativeUi.LR_LOADFROMFILE | NativeUi.LR_DEFAULTSIZE);
        _taskbarCreated = NativeUi.RegisterWindowMessage("TaskbarCreated");
        Add();
    }

    public event EventHandler<TrayCommand>? Command;

    /// <summary>Reflected as a check mark on the pause entry.</summary>
    public bool Paused { get; set; }

    /// <summary>Enables the Cloud PC entries.</summary>
    public bool HasSession { get; set; }

    public void SetTooltip(string text)
    {
        _tooltip = text.Length > 127 ? text[..127] : text;
        var data = Data(NativeUi.NIF_TIP);
        NativeUi.Shell_NotifyIcon(NativeUi.NIM_MODIFY, ref data);
    }

    private void Add()
    {
        var data = Data(NativeUi.NIF_MESSAGE | NativeUi.NIF_ICON | NativeUi.NIF_TIP);
        NativeUi.Shell_NotifyIcon(NativeUi.NIM_ADD, ref data);
    }

    private NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            switch ((uint)NativeUi.LowWord(lParam))
            {
                case NativeUi.WM_LBUTTONUP:
                    Command?.Invoke(this, TrayCommand.OpenDigest);
                    break;
                case NativeUi.WM_RBUTTONUP:
                    ShowMenu();
                    break;
            }
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated)
        {
            Add(); // Explorer restarted
            return IntPtr.Zero;
        }
        return NativeUi.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = NativeUi.CreatePopupMenu();
        NativeUi.AppendMenu(menu, NativeUi.MF_STRING, (UIntPtr)TrayCommand.OpenDigest, "Abrir resumen");
        NativeUi.AppendMenu(menu, NativeUi.MF_STRING, (UIntPtr)TrayCommand.OpenSettings, "Ajustes");
        NativeUi.AppendMenu(menu, NativeUi.MF_SEPARATOR, UIntPtr.Zero, null);
        uint session = HasSession ? NativeUi.MF_STRING : NativeUi.MF_STRING | NativeUi.MF_GRAYED;
        NativeUi.AppendMenu(menu, session, (UIntPtr)TrayCommand.ReturnToRemote, "Volver al Cloud PC");
        NativeUi.AppendMenu(menu, session, (UIntPtr)TrayCommand.MinimizeRemote, "Minimizar Cloud PC");
        NativeUi.AppendMenu(menu, NativeUi.MF_SEPARATOR, UIntPtr.Zero, null);
        NativeUi.AppendMenu(menu, NativeUi.MF_STRING | (Paused ? NativeUi.MF_CHECKED : 0), (UIntPtr)TrayCommand.TogglePause, "Pausar avisos");
        NativeUi.AppendMenu(menu, NativeUi.MF_SEPARATOR, UIntPtr.Zero, null);
        NativeUi.AppendMenu(menu, NativeUi.MF_STRING, (UIntPtr)TrayCommand.Exit, "Salir");

        NativeUi.GetCursorPos(out var pt);
        NativeUi.SetForegroundWindow(_hwnd); // required for the menu to close when clicking elsewhere
        int cmd = NativeUi.TrackPopupMenu(menu, NativeUi.TPM_RIGHTBUTTON | NativeUi.TPM_RETURNCMD | NativeUi.TPM_BOTTOMALIGN, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        NativeUi.DestroyMenu(menu);
        if (cmd != 0) Command?.Invoke(this, (TrayCommand)cmd);
    }

    public void Dispose()
    {
        var data = Data(0);
        NativeUi.Shell_NotifyIcon(NativeUi.NIM_DELETE, ref data);
        NativeUi.DestroyWindow(_hwnd);
        if (_icon != IntPtr.Zero) NativeUi.DestroyIcon(_icon);
        GC.KeepAlive(_wndProc);
    }
}
