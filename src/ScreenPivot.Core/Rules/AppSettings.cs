using ScreenPivot.Core.Localization;
using ScreenPivot.Core.Model;

namespace ScreenPivot.Core.Rules;

public enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>Screen edge the ScreenPivot panel slides out from.</summary>
public enum PanelEdge
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>What happens to the remote session when opening a host app from a notification.</summary>
public enum SwitchMode
{
    /// <summary>The app comes in front of the session, which stays full screen behind it.</summary>
    BringToFront,
    /// <summary>The session is minimized so the whole host desktop is visible.</summary>
    MinimizeRemote,
}

/// <summary>Assigns <see cref="Priority"/> to events whose app and text match.</summary>
public sealed class PriorityRule
{
    public string Name { get; set; } = "";

    /// <summary>Case-insensitive substring of the app display name; empty matches any app.</summary>
    public string App { get; set; } = "";

    /// <summary>Any of these in the title or body; empty matches any text.</summary>
    public List<string> Keywords { get; set; } = new();

    public Priority Priority { get; set; } = Priority.Normal;

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Watches window titles of apps that do not send notifications. When <see cref="Pattern"/> has a numeric first
/// group (e.g. <c>^\((\d+)\)</c>) it fires when the number goes up; otherwise when the matched text appears or changes.
/// </summary>
public sealed class TitleRule
{
    public string Name { get; set; } = "";

    /// <summary>Case-insensitive substring of the process name or app name; empty matches any app.</summary>
    public string App { get; set; } = "";

    /// <summary>.NET regular expression, matched case-insensitively against the window title.</summary>
    public string Pattern { get; set; } = "";

    public Priority Priority { get; set; } = Priority.Normal;

    public bool Enabled { get; set; } = true;
}

public sealed class AppSettings
{
    public List<PriorityRule> Rules { get; set; } = DefaultRules();

    public List<TitleRule> TitleRules { get; set; } = DefaultTitleRules();

    /// <summary>Report apps that flash their taskbar button (they want attention but sent no notification).</summary>
    public bool WatchFlashes { get; set; } = true;

    public bool WatchTitles { get; set; } = true;

    /// <summary>Sender names; Outlook and Teams put the sender in the toast title.</summary>
    public List<string> VipSenders { get; set; } = new();

    public List<string> CriticalKeywords { get; set; } = new() { "urgente", "urgent", "incidente", "outage" };

    /// <summary>Apps whose notifications are dropped entirely.</summary>
    public List<string> MutedApps { get; set; } = new() { "ScreenPivot", "Windows App" };

    public OverlayCorner Corner { get; set; } = OverlayCorner.BottomRight;

    /// <summary>Monitor index for the overlay; -1 picks the first monitor covered by the remote session.</summary>
    public int MonitorIndex { get; set; } = -1;

    /// <summary>Treat a maximized (taskbar still visible) remote window as remote mode, not only full screen.</summary>
    public bool IncludeMaximized { get; set; }

    /// <summary>UI language ("es-ES", "en-US"); empty follows the Windows display language. Applied at startup.</summary>
    public string Language { get; set; } = "";

    public PanelEdge PanelEdge { get; set; } = PanelEdge.Right;

    /// <summary>Monitor index for the panel; -1 opens it on the monitor under the cursor.</summary>
    public int PanelMonitorIndex { get; set; } = -1;

    public SwitchMode SwitchMode { get; set; } = SwitchMode.BringToFront;

    /// <summary>Keep a small "go to host" handle on screen in the remote session even with nothing pending.</summary>
    public bool ShowQuickAccess { get; set; } = true;

    /// <summary>Master switch for <see cref="Sounds"/>.</summary>
    public bool SoundEnabled { get; set; } = true;

    public PrioritySounds Sounds { get; set; } = new();

    public bool ShowDigestOnReturn { get; set; } = true;

    public int HighBannerSeconds { get; set; } = 6;

    public int RetentionHours { get; set; } = 24;

    public bool Paused { get; set; }

    public bool OnboardingCompleted { get; set; }

    /// <summary>Rule names follow the UI language at the time they are created; after that they are user data.</summary>
    public static List<PriorityRule> DefaultRules() => new()
    {
        new() { Name = CoreText.Get("Rule_TeamsCalls"), App = "Teams", Keywords = { "llamada", "llamando", "calling", "call from", "incoming call" }, Priority = Priority.Critical },
        new() { Name = CoreText.Get("Rule_MeetingStarting"), Keywords = { "comienza ahora", "starting now", "starts in", "comienza en" }, Priority = Priority.High },
        new() { Name = CoreText.Get("Rule_TeamsMessages"), App = "Teams", Priority = Priority.High },
        new() { Name = CoreText.Get("Rule_OutlookMail"), App = "Outlook", Priority = Priority.Normal },
    };

    public static List<TitleRule> DefaultTitleRules() => new()
    {
        new()
        {
            Name = CoreText.Get("TitleRule_Build"),
            Pattern = @"\b(build|compilación|pipeline|deploy|despliegue)\b.*\b(succeeded|failed|completed|correcta|fallida|falló|terminad[oa])\b",
            Priority = Priority.High,
        },
        // Off by default: apps that already send notifications would be reported twice.
        new() { Name = CoreText.Get("TitleRule_Unread"), Pattern = @"^\((\d+)\)", Priority = Priority.Normal, Enabled = false },
    };
}
