using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;

namespace ScreenPivot.App.Pages;

/// <summary>Editable row for a <see cref="PriorityRule"/>; keywords are edited as comma-separated text.</summary>
public sealed class RuleRow
{
    public static IReadOnlyList<string> PriorityNames => Format.PriorityNames;

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string App { get; set; } = "";
    public string Keywords { get; set; } = "";
    public int PriorityIndex { get; set; } = (int)Priority.Normal;

    public static RuleRow From(PriorityRule rule) => new()
    {
        Enabled = rule.Enabled,
        Name = rule.Name,
        App = rule.App,
        Keywords = string.Join(", ", rule.Keywords),
        PriorityIndex = (int)rule.Priority,
    };

    public PriorityRule ToRule() => new()
    {
        Enabled = Enabled,
        Name = Name.Trim(),
        App = App.Trim(),
        Keywords = Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        Priority = (Priority)Math.Clamp(PriorityIndex, 0, PriorityNames.Count - 1),
    };

    public bool IsEmpty => Name.Trim().Length == 0 && App.Trim().Length == 0 && Keywords.Trim().Length == 0;
}
