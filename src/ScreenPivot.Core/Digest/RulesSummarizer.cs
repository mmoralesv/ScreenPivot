using ScreenPivot.Core.Model;

namespace ScreenPivot.Core.Digest;

public sealed record AppGroup(string App, int Count, Priority MaxPriority, IReadOnlyList<HostEvent> Latest);

public sealed record DigestSummary(
    int Total,
    IReadOnlyDictionary<Priority, int> CountsByPriority,
    IReadOnlyList<AppGroup> Groups,
    string Headline);

/// <summary>Deterministic, local summary: groups by app, most important and busiest apps first.</summary>
public static class RulesSummarizer
{
    public const int LatestPerApp = 5;

    public static DigestSummary Summarize(IEnumerable<HostEvent> events)
    {
        var list = events.Where(e => e.Priority > Priority.Ignore).ToList();

        var counts = list.GroupBy(e => e.Priority).ToDictionary(g => g.Key, g => g.Count());

        var groups = list
            .GroupBy(e => e.App, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new AppGroup(
                g.First().App,
                g.Count(),
                g.Max(e => e.Priority),
                g.OrderByDescending(e => e.Priority).ThenByDescending(e => e.Timestamp).Take(LatestPerApp).ToList()))
            .OrderByDescending(g => g.MaxPriority)
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.App, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new DigestSummary(list.Count, counts, groups, Headline(list.Count, counts, groups));
    }

    public static string Headline(int total, IReadOnlyDictionary<Priority, int> counts, IReadOnlyList<AppGroup> groups)
    {
        if (total == 0) return "Sin avisos nuevos.";

        var parts = new List<string>();
        if (counts.TryGetValue(Priority.Critical, out var c)) parts.Add(c == 1 ? "1 crítico" : $"{c} críticos");
        if (counts.TryGetValue(Priority.High, out var h)) parts.Add(h == 1 ? "1 alto" : $"{h} altos");

        var apps = string.Join(", ", groups.Take(3).Select(g => $"{g.App} ({g.Count})"));
        if (groups.Count > 3) apps += $" y {groups.Count - 3} más";

        var noun = total == 1 ? "1 aviso" : $"{total} avisos";
        return parts.Count == 0 ? $"{noun}: {apps}." : $"{noun} ({string.Join(", ", parts)}): {apps}.";
    }

    /// <summary>Compact text for the overlay ticker, e.g. "3 avisos · 1 crítico".</summary>
    public static string Ticker(IReadOnlyCollection<HostEvent> pending)
    {
        if (pending.Count == 0) return "";
        var critical = pending.Count(e => e.Priority == Priority.Critical);
        var high = pending.Count(e => e.Priority == Priority.High);
        var text = pending.Count == 1 ? "1 aviso" : $"{pending.Count} avisos";
        if (critical > 0) text += critical == 1 ? " · 1 crítico" : $" · {critical} críticos";
        if (high > 0) text += high == 1 ? " · 1 alto" : $" · {high} altos";
        return text;
    }
}
