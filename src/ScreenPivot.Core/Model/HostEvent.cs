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

    /// <summary>Why the priority engine chose <see cref="Priority"/>, shown to the user.</summary>
    public string Reason { get; init; } = "";

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
