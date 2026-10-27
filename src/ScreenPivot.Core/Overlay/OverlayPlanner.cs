namespace ScreenPivot.Core.Overlay;

public enum OverlayView
{
    Hidden,
    /// <summary>One high/critical event with Open and Close.</summary>
    Banner,
    /// <summary>Pending count while working in the remote session; click opens the ScreenPivot panel.</summary>
    Ticker,
    /// <summary>Nothing pending; small "go to host" handle while in the remote session.</summary>
    QuickAccess,
    /// <summary>The user is on the host while a session exists; click goes back to it.</summary>
    ReturnToRemote,
}

public readonly record struct OverlayInputs(
    bool Remote,
    bool HostForeground,
    bool Parked,
    bool HasBanner,
    bool TestBanner,
    int PendingCount,
    bool Paused,
    bool ShowQuickAccess,
    bool PanelOpen = false);

public static class OverlayPlanner
{
    /// <param name="i">
    /// Remote: a session covers the screen. HostForeground: the foreground window is not the session.
    /// Parked: the session was minimized by ScreenPivot to show a host app.
    /// </param>
    public static OverlayView Decide(OverlayInputs i)
    {
        if (i.TestBanner) return OverlayView.Banner;
        // The open panel already lists events and has the switch buttons; the overlay would only cover it.
        if (i.PanelOpen) return OverlayView.Hidden;
        if (i.HasBanner && i.Remote && !i.Paused) return OverlayView.Banner;
        // Switching back stays available while paused: pausing silences alerts, not navigation.
        if (i.Parked || (i.Remote && i.HostForeground)) return OverlayView.ReturnToRemote;
        if (!i.Remote) return OverlayView.Hidden;
        if (!i.Paused && i.PendingCount > 0) return OverlayView.Ticker;
        return i.ShowQuickAccess ? OverlayView.QuickAccess : OverlayView.Hidden;
    }
}
