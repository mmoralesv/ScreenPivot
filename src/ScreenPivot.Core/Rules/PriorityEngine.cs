using ScreenPivot.Core.Localization;
using ScreenPivot.Core.Model;

namespace ScreenPivot.Core.Rules;

/// <param name="Detail">The VIP, keyword or rule name that matched; empty for other kinds.</param>
public readonly record struct PriorityDecision(Priority Priority, ReasonKind Kind, string Detail = "")
{
    public string Reason => CoreText.Reason(Kind, Detail);
}

/// <summary>
/// Order of precedence: muted app → VIP sender → critical keyword → highest of the matching app rules and the
/// source's own rule (title rules) → Normal.
/// </summary>
public static class PriorityEngine
{
    public static PriorityDecision Evaluate(HostEvent e, AppSettings settings)
    {
        if (settings.MutedApps.FirstOrDefault(m => Contains(e.App, m)) is { } muted)
            return new(Priority.Ignore, ReasonKind.Muted, muted);

        if (settings.VipSenders.FirstOrDefault(v => Contains(e.Title, v)) is { } vip)
            return new(Priority.Critical, ReasonKind.Vip, vip);

        if (settings.CriticalKeywords.FirstOrDefault(k => Contains(e.Title, k) || Contains(e.Body, k)) is { } keyword)
            return new(Priority.Critical, ReasonKind.Keyword, keyword);

        var best = settings.Rules
            .Where(r => r.Enabled && Matches(r, e))
            .OrderByDescending(r => r.Priority)
            .FirstOrDefault();
        if (e.SourcePriority is { } source && (best is null || source > best.Priority))
            return new(source, ReasonKind.TitleRule, e.SourceRule);
        return best is null
            ? new(Priority.Normal, ReasonKind.NoRule)
            : new(best.Priority, ReasonKind.Rule, best.Name);
    }

    public static bool Matches(PriorityRule rule, HostEvent e)
    {
        if (!string.IsNullOrWhiteSpace(rule.App) && !Contains(e.App, rule.App)) return false;
        return rule.Keywords.Count == 0 || rule.Keywords.Any(k => Contains(e.Title, k) || Contains(e.Body, k));
    }

    private static bool Contains(string text, string fragment) =>
        !string.IsNullOrWhiteSpace(fragment) && text.Contains(fragment.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
