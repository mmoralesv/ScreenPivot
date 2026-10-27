using System.Globalization;
using ScreenPivot.Core.Localization;
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
    private const string Separator = " · ";

    public static DigestSummary Summarize(IEnumerable<HostEvent> events, CultureInfo? culture = null)
    {
        var list = events.Where(e => e.Priority > Priority.Ignore).ToList();

        var counts = list.GroupBy(e => e.Priority).ToDictionary(g => g.Key, g => g.Count());

        var groups = list
            .GroupBy(e => e.DisplayApp, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new AppGroup(
                g.Key,
                g.Count(),
                g.Max(e => e.Priority),
                g.OrderByDescending(e => e.Priority).ThenByDescending(e => e.Timestamp).Take(LatestPerApp).ToList()))
            .OrderByDescending(g => g.MaxPriority)
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.App, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new DigestSummary(list.Count, counts, groups, Headline(list.Count, counts, groups, culture));
    }

    public static string Headline(int total, IReadOnlyDictionary<Priority, int> counts, IReadOnlyList<AppGroup> groups, CultureInfo? culture = null)
    {
        if (total == 0) return CoreText.Get("Summary_None", culture);

        var parts = new List<string>();
        if (counts.TryGetValue(Priority.Critical, out var c)) parts.Add(CoreText.Count("Critical", c, culture));
        if (counts.TryGetValue(Priority.High, out var h)) parts.Add(CoreText.Count("High", h, culture));

        var apps = string.Join(", ", groups.Take(3).Select(g => $"{g.App} ({g.Count})"));
        if (groups.Count > 3) apps = CoreText.Format("Summary_AndMore", culture, apps, groups.Count - 3);

        var noun = CoreText.Count("Alert", total, culture);
        return parts.Count == 0
            ? CoreText.Format("Summary_Headline", culture, noun, apps)
            : CoreText.Format("Summary_HeadlineWithParts", culture, noun, string.Join(", ", parts), apps);
    }

    /// <summary>Compact text for the overlay ticker, e.g. "3 avisos · 1 crítico".</summary>
    public static string Ticker(IReadOnlyCollection<HostEvent> pending, CultureInfo? culture = null)
    {
        if (pending.Count == 0) return "";
        var critical = pending.Count(e => e.Priority == Priority.Critical);
        var high = pending.Count(e => e.Priority == Priority.High);
        var text = CoreText.Count("Alert", pending.Count, culture);
        if (critical > 0) text += Separator + CoreText.Count("Critical", critical, culture);
        if (high > 0) text += Separator + CoreText.Count("High", high, culture);
        return text;
    }
}
