using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;

namespace ScreenPivot.App.Pages;

/// <summary>Editable row for a <see cref="TitleRule"/>.</summary>
public sealed class TitleRuleRow
{
    public static IReadOnlyList<string> PriorityNames { get; } = Format.PriorityNames;

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string App { get; set; } = "";
    public string Pattern { get; set; } = "";
    public int PriorityIndex { get; set; } = (int)Priority.Normal;

    public static TitleRuleRow From(TitleRule rule) => new()
    {
        Enabled = rule.Enabled,
        Name = rule.Name,
        App = rule.App,
        Pattern = rule.Pattern,
        PriorityIndex = (int)rule.Priority,
    };

    public TitleRule ToRule() => new()
    {
        Enabled = Enabled,
        Name = Name.Trim(),
        App = App.Trim(),
        Pattern = Pattern.Trim(),
        Priority = (Priority)Math.Clamp(PriorityIndex, 0, PriorityNames.Count - 1),
    };

    public bool IsEmpty => Name.Trim().Length == 0 && Pattern.Trim().Length == 0;
}
