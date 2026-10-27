using System.Text.Json.Serialization;
using ScreenPivot.Core.Localization;

namespace ScreenPivot.Core.Model;

public enum Priority
{
    Ignore = 0,
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4,
}

public enum EventOrigin
{
    Toast,
    WindowFlash,
    TitleChange,
    Test,
}

/// <summary>Why an event got its priority. Stored as data so the text follows the UI language when shown.</summary>
public enum ReasonKind
{
    None,
    NoRule,
    Muted,
    Vip,
    Keyword,
    Rule,
    TitleRule,
    Test,
}

/// <summary>Something that happened in an app on the host, normalized from any source.</summary>
public sealed record HostEvent(
    string Id,
    string App,
    string Title,
    string Body,
    DateTimeOffset Timestamp,
    EventOrigin Origin)
{
    public Priority Priority { get; init; } = Priority.Normal;

    public ReasonKind ReasonKind { get; init; }

    /// <summary>The VIP, keyword or rule name that matched; empty for other kinds.</summary>
    public string ReasonDetail { get; init; } = "";

    /// <summary>Why the priority engine chose <see cref="Priority"/>, in the current UI language.</summary>
    [JsonIgnore]
    public string Reason => CoreText.Reason(ReasonKind, ReasonDetail);

    /// <summary><see cref="App"/>, or a localized "unknown app" when the source could not name it.</summary>
    [JsonIgnore]
    public string DisplayApp => App.Length > 0 ? App : CoreText.Get("Event_UnknownApp");

    /// <summary><see cref="Body"/>, or the localized flash explanation (flashes carry no text of their own).</summary>
    [JsonIgnore]
    public string DisplayBody => Body.Length == 0 && Origin == EventOrigin.WindowFlash ? CoreText.Get("Event_FlashBody") : Body;

    public bool ReceivedWhileRemote { get; init; }

    /// <summary>AppUserModelID of the source app when known; used to find or launch its window.</summary>
    public string AppId { get; init; } = "";

    /// <summary>HWND of the window that produced the event (flash, title change); 0 for toasts.</summary>
    public long WindowHandle { get; init; }

    /// <summary>Priority requested by the source's own rule (title rules); competes with the app rules.</summary>
    public Priority? SourcePriority { get; init; }

    public string SourceRule { get; init; } = "";

    public string DedupKey => $"{App}\u001f{Title}\u001f{Body}";
}
