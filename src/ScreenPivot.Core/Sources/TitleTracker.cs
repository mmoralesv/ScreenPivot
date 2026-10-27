using System.Globalization;
using System.Text.RegularExpressions;
using ScreenPivot.Core.Rules;

namespace ScreenPivot.Core.Sources;

public readonly record struct TitleMatch(TitleRule Rule, string Title, string Matched);

/// <summary>
/// Decides which title changes are news. The first title seen for a window is only a baseline, so starting
/// ScreenPivot or opening a window never fires. Not thread-safe; used from the UI thread.
/// </summary>
public sealed class TitleTracker
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private readonly Dictionary<long, string> _titles = new();
    private readonly Dictionary<(long Window, TitleRule Rule), string> _matches = new();
    private readonly Dictionary<string, Regex?> _regexCache = new();

    public void Seed(long window, string title, IEnumerable<TitleRule> rules, string app)
    {
        _titles[window] = title;
        foreach (var rule in Applicable(rules, app))
            if (TryMatch(rule, title, out var matched)) _matches[(window, rule)] = matched;
    }

    public IReadOnlyList<TitleMatch> OnTitleChanged(long window, string title, IEnumerable<TitleRule> rules, string app)
    {
        if (!_titles.TryGetValue(window, out var previous))
        {
            Seed(window, title, rules, app);
            return Array.Empty<TitleMatch>();
        }
        if (previous == title) return Array.Empty<TitleMatch>();
        _titles[window] = title;

        var fired = new List<TitleMatch>();
        foreach (var rule in Applicable(rules, app))
        {
            var key = (window, rule);
            if (!TryMatch(rule, title, out var matched))
            {
                _matches.Remove(key);
                continue;
            }
            _matches.TryGetValue(key, out var last);
            _matches[key] = matched;
            if (IsNews(last, matched)) fired.Add(new TitleMatch(rule, title, matched));
        }
        return fired;
    }

    public void Forget(long window)
    {
        _titles.Remove(window);
        foreach (var key in _matches.Keys.Where(k => k.Window == window).ToList()) _matches.Remove(key);
    }

    /// <summary>Counters fire only when they go up; plain matches fire when they appear or change.</summary>
    private static bool IsNews(string? last, string matched)
    {
        if (last is null) return true;
        if (long.TryParse(matched, NumberStyles.Integer, CultureInfo.InvariantCulture, out var now))
            return !long.TryParse(last, NumberStyles.Integer, CultureInfo.InvariantCulture, out var before) || now > before;
        return !string.Equals(last, matched, StringComparison.CurrentCultureIgnoreCase);
    }

    private static IEnumerable<TitleRule> Applicable(IEnumerable<TitleRule> rules, string app) =>
        rules.Where(r => r.Enabled && r.Pattern.Length > 0 &&
                         (string.IsNullOrWhiteSpace(r.App) || app.Contains(r.App.Trim(), StringComparison.CurrentCultureIgnoreCase)));

    /// <param name="matched">The first capture group when it is a number (the counter), else the whole match.</param>
    private bool TryMatch(TitleRule rule, string title, out string matched)
    {
        matched = "";
        var regex = Compile(rule.Pattern);
        if (regex is null) return false;
        try
        {
            var m = regex.Match(title);
            if (!m.Success) return false;
            var first = m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : "";
            matched = long.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? first : m.Value;
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private Regex? Compile(string pattern)
    {
        if (_regexCache.TryGetValue(pattern, out var regex)) return regex;
        regex = TryCreate(pattern, out _);
        _regexCache[pattern] = regex;
        return regex;
    }

    /// <summary>Null and an error message when the pattern is not a valid regular expression.</summary>
    public static Regex? TryCreate(string pattern, out string error)
    {
        try
        {
            error = "";
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException e)
        {
            error = e.Message;
            return null;
        }
    }
}
