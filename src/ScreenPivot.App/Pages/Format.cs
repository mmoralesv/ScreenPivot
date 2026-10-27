using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ScreenPivot.Core.Localization;
using ScreenPivot.Core.Model;

namespace ScreenPivot.App.Pages;

/// <summary>x:Bind helpers for page templates. Text follows the UI language chosen at startup.</summary>
public static class Format
{
    private static IReadOnlyList<string>? _priorityNames;

    /// <summary>
    /// Priority names indexed by <see cref="Priority"/> value, for the rule combo boxes. Built once (the language is
    /// fixed at startup) and always the same instance: a new list would reset the TwoWay-bound SelectedIndex to -1.
    /// </summary>
    public static IReadOnlyList<string> PriorityNames =>
        _priorityNames ??= Enum.GetValues<Priority>().Select(p => CoreText.Priority(p)).ToList();

    public static string PriorityLabel(Priority p) => CoreText.Priority(p);

    public static Brush PriorityBrush(Priority p) => new SolidColorBrush(p switch
    {
        Priority.Critical => ColorHelper.FromArgb(255, 232, 17, 35),
        Priority.High => ColorHelper.FromArgb(255, 247, 99, 12),
        Priority.Normal => ColorHelper.FromArgb(255, 0, 120, 212),
        _ => Colors.Gray,
    });

    /// <summary>Short time today ("21:41" / "9:41 PM"); day and month plus time on other days.</summary>
    public static string Time(DateTimeOffset t)
    {
        var local = t.LocalDateTime;
        var culture = CultureInfo.CurrentCulture;
        return local.Date == DateTime.Today
            ? local.ToString("t", culture)
            : $"{local.ToString(culture.DateTimeFormat.MonthDayPattern.Contains("MMMM") ? "d MMM" : "M/d", culture)} {local.ToString("t", culture)}";
    }

    public static string Count(int n) => CoreText.Count("Alert", n);

    public static Visibility VisibleIfText(string text) => string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
}
