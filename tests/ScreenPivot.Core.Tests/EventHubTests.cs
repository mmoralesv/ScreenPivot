using ScreenPivot.Core.Digest;
using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;
using ScreenPivot.Core.Storage;
using Xunit;

namespace ScreenPivot.Core.Tests;

public class EventHubTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly AppSettings _settings = new();
    private readonly DigestStore _store = new(null, TimeSpan.FromHours(24));
    private readonly EventHub _hub;

    public EventHubTests() => _hub = new EventHub(() => _settings, _store);

    private static HostEvent Event(string app, string title, DateTimeOffset at, string id = "") =>
        new(id == "" ? Guid.NewGuid().ToString() : id, app, title, "", at, EventOrigin.Toast);

    [Fact]
    public void EventsOutsideRemoteAreStoredButNotPending()
    {
        var accepted = _hub.Ingest(Event("Outlook", "Hola", T0));
        Assert.NotNull(accepted);
        Assert.False(accepted!.ReceivedWhileRemote);
        Assert.Empty(_hub.Pending());
        Assert.Single(_store.All());
    }

    [Fact]
    public void EventsDuringRemoteArePendingAndReturnedOnExit()
    {
        _hub.EnterRemote(T0);
        _hub.Ingest(Event("Outlook", "Uno", T0.AddMinutes(1)));
        _hub.Ingest(Event("Microsoft Teams", "Dos", T0.AddMinutes(2)));

        Assert.Equal(2, _hub.Pending().Count);
        var session = _hub.ExitRemote();
        Assert.Equal(2, session.Count);
        Assert.False(_hub.IsRemote);
    }

    [Fact]
    public void DuplicatesWithinWindowAreDropped()
    {
        Assert.NotNull(_hub.Ingest(Event("Outlook", "Igual", T0)));
        Assert.Null(_hub.Ingest(Event("Outlook", "Igual", T0.AddSeconds(30))));
        Assert.NotNull(_hub.Ingest(Event("Outlook", "Igual", T0.AddSeconds(30) + EventHub.DedupWindow + TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void IgnoredEventsAreNotStored()
    {
        Assert.Null(_hub.Ingest(Event("Windows App", "Sesión", T0)));
        Assert.Empty(_store.All());
    }

    [Fact]
    public void MarkReviewedClearsPending()
    {
        _hub.EnterRemote(T0);
        _hub.Ingest(Event("Outlook", "Uno", T0.AddMinutes(1)));
        _hub.MarkReviewed(T0.AddMinutes(2));
        Assert.Empty(_hub.Pending());
        _hub.Ingest(Event("Outlook", "Dos", T0.AddMinutes(3)));
        Assert.Single(_hub.Pending());
    }

    [Fact]
    public void PausedEventsAreNotPending()
    {
        _settings.Paused = true;
        _hub.EnterRemote(T0);
        _hub.Ingest(Event("Outlook", "Uno", T0.AddMinutes(1)));
        Assert.Empty(_hub.Pending());
    }

    [Fact]
    public void AcceptedCarriesPriorityAndReason()
    {
        HostEvent? raised = null;
        _hub.Accepted += (_, e) => raised = e;
        _hub.Ingest(Event("Microsoft Teams", "Luis te está llamando", T0));
        Assert.Equal(Priority.Critical, raised!.Priority);
        Assert.StartsWith("Regla:", raised.Reason);
    }

    [Fact]
    public void SummaryGroupsByAppMostImportantFirst()
    {
        _hub.EnterRemote(T0);
        _hub.Ingest(Event("Outlook", "a", T0.AddSeconds(1)));
        _hub.Ingest(Event("Outlook", "b", T0.AddSeconds(2)));
        _hub.Ingest(Event("Outlook", "c", T0.AddSeconds(3)));
        _hub.Ingest(Event("Microsoft Teams", "Luis te está llamando", T0.AddSeconds(4)));

        var summary = RulesSummarizer.Summarize(_hub.ExitRemote());
        Assert.Equal(4, summary.Total);
        Assert.Equal("Microsoft Teams", summary.Groups[0].App);
        Assert.Equal(Priority.Critical, summary.Groups[0].MaxPriority);
        Assert.Equal(3, summary.Groups[1].Count);
        Assert.Equal("4 avisos (1 crítico): Microsoft Teams (1), Outlook (3).", summary.Headline);
    }

    [Fact]
    public void LogNeverContainsVipNames()
    {
        Assert.Equal("VIP", EventHub.LogReason("VIP: Ana Pérez"));
        Assert.Equal("Regla: Mensajes de Teams", EventHub.LogReason("Regla: Mensajes de Teams"));
    }

    [Fact]
    public void TickerText()
    {
        var events = new[]
        {
            Event("a", "x", T0) with { Priority = Priority.Critical },
            Event("b", "y", T0) with { Priority = Priority.High },
            Event("c", "z", T0) with { Priority = Priority.Normal },
        };
        Assert.Equal("3 avisos · 1 crítico · 1 alto", RulesSummarizer.Ticker(events));
        Assert.Equal("", RulesSummarizer.Ticker(Array.Empty<HostEvent>()));
    }
}
