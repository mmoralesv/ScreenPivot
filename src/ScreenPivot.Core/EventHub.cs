using ScreenPivot.Core.Diagnostics;
using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;
using ScreenPivot.Core.Storage;

namespace ScreenPivot.Core;

/// <summary>
/// Single entry point for events from every source: prioritizes, de-duplicates, stores and tracks what arrived
/// during the current remote session. Thread-safe; <see cref="Accepted"/> fires on the caller's thread.
/// </summary>
public sealed class EventHub
{
    public static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Func<AppSettings> _settings;
    private readonly DigestStore _store;
    private readonly Dictionary<string, DateTimeOffset> _recent = new();
    private DateTimeOffset? _remoteSince;
    private DateTimeOffset _reviewedUntil = DateTimeOffset.MinValue;

    public EventHub(Func<AppSettings> settings, DigestStore store)
    {
        _settings = settings;
        _store = store;
    }

    public event EventHandler<HostEvent>? Accepted;

    public bool IsRemote
    {
        get { lock (_gate) return _remoteSince is not null; }
    }

    public DateTimeOffset? RemoteSince
    {
        get { lock (_gate) return _remoteSince; }
    }

    public void EnterRemote(DateTimeOffset now)
    {
        lock (_gate) _remoteSince ??= now;
    }

    /// <summary>Leaves remote mode and returns what arrived during it.</summary>
    public IReadOnlyList<HostEvent> ExitRemote()
    {
        DateTimeOffset since;
        lock (_gate)
        {
            if (_remoteSince is null) return Array.Empty<HostEvent>();
            since = _remoteSince.Value;
            _remoteSince = null;
        }
        return _store.Since(since).Where(e => e.ReceivedWhileRemote).ToList();
    }

    /// <summary>Events received while remote that the user has not reviewed yet (drives the ticker).</summary>
    public IReadOnlyList<HostEvent> Pending()
    {
        DateTimeOffset since;
        lock (_gate) since = _reviewedUntil;
        return _store.Since(since).Where(e => e.ReceivedWhileRemote && e.Timestamp > since).ToList();
    }

    public void MarkReviewed(DateTimeOffset now)
    {
        lock (_gate) _reviewedUntil = now;
    }

    /// <returns>The accepted event, or null when it was ignored, paused or a duplicate.</returns>
    public HostEvent? Ingest(HostEvent raw)
    {
        var settings = _settings();
        var decision = PriorityEngine.Evaluate(raw, settings);
        if (decision.Priority == Priority.Ignore)
        {
            DiagnosticsLog.Write($"ignored {raw.Origin} app='{raw.App}' ({LogReason(decision.Reason)})");
            return null;
        }

        HostEvent accepted;
        lock (_gate)
        {
            if (IsDuplicate(raw))
            {
                DiagnosticsLog.Write($"duplicate {raw.Origin} app='{raw.App}'");
                return null;
            }
            accepted = raw with
            {
                Priority = decision.Priority,
                Reason = decision.Reason,
                ReceivedWhileRemote = _remoteSince is not null && !settings.Paused,
            };
        }

        _store.Add(accepted);
        DiagnosticsLog.Write($"accepted {accepted.Origin} app='{accepted.App}' priority={accepted.Priority} remote={accepted.ReceivedWhileRemote} ({LogReason(accepted.Reason)})");
        Accepted?.Invoke(this, accepted);
        return accepted;
    }

    /// <summary>VIP reasons carry a person's name; the log keeps only that it was a VIP match.</summary>
    internal static string LogReason(string reason) =>
        reason.StartsWith("VIP:", StringComparison.Ordinal) ? "VIP" : reason;

    private bool IsDuplicate(HostEvent e)
    {
        foreach (var stale in _recent.Where(kv => e.Timestamp - kv.Value > DedupWindow).Select(kv => kv.Key).ToList())
            _recent.Remove(stale);

        if (_recent.TryGetValue(e.DedupKey, out var last) && e.Timestamp - last <= DedupWindow) return true;
        _recent[e.DedupKey] = e.Timestamp;
        return false;
    }
}
