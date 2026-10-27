using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;
using ScreenPivot.Core.Sources;
using Xunit;

namespace ScreenPivot.Core.Tests;

public class TitleTrackerTests
{
    private static readonly TitleRule Counter = new() { Name = "Sin leer", Pattern = @"^\((\d+)\)" };
    private static readonly TitleRule Build = AppSettings.DefaultTitleRules()[0];

    private readonly TitleTracker _tracker = new();

    private IReadOnlyList<TitleMatch> Change(string title, string app = "Chrome", long window = 1, params TitleRule[] rules) =>
        _tracker.OnTitleChanged(window, title, rules.Length == 0 ? new[] { Counter, Build } : rules, app);

    [Fact]
    public void FirstTitleIsOnlyABaseline()
    {
        Assert.Empty(Change("(3) WhatsApp"));
        Assert.Empty(Change("(3) WhatsApp"));
    }

    [Fact]
    public void CounterFiresOnlyWhenItGoesUp()
    {
        _tracker.Seed(1, "(3) WhatsApp", new[] { Counter }, "Chrome");
        Assert.Single(Change("(5) WhatsApp", rules: Counter));
        Assert.Empty(Change("(2) WhatsApp", rules: Counter));
        Assert.Single(Change("(4) WhatsApp", rules: Counter));
    }

    [Fact]
    public void CounterReappearingAfterReadFires()
    {
        _tracker.Seed(1, "(2) Slack", new[] { Counter }, "Chrome");
        Assert.Empty(Change("Slack", rules: Counter));
        Assert.Single(Change("(1) Slack", rules: Counter));
    }

    [Fact]
    public void TextMatchFiresWhenItAppearsOrChanges()
    {
        _tracker.Seed(1, "MyApp - Building...", new[] { Build }, "Visual Studio");
        var fired = Change("MyApp - Build succeeded", "Visual Studio", rules: Build);
        Assert.Equal(Build, Assert.Single(fired).Rule);
        Assert.Empty(Change("MyApp - Build succeeded ", "Visual Studio", rules: Build)); // same match text
        Assert.Single(Change("MyApp - Build failed", "Visual Studio", rules: Build));
    }

    [Fact]
    public void SpanishBuildMessagesMatch()
    {
        _tracker.Seed(1, "Pipeline en curso", new[] { Build }, "Edge");
        Assert.Single(Change("Pipeline terminado con éxito", "Edge", rules: Build));
    }

    [Fact]
    public void RuleAppFilterAndDisabledRules()
    {
        var onlyTeams = new TitleRule { Pattern = @"^\((\d+)\)", App = "Teams" };
        var disabled = new TitleRule { Pattern = @"^\((\d+)\)", Enabled = false };
        _tracker.Seed(1, "(1) x", new[] { onlyTeams, disabled }, "Chrome");
        Assert.Empty(Change("(9) x", "Chrome", rules: new[] { onlyTeams, disabled }));
    }

    [Fact]
    public void WindowsAreTrackedSeparately()
    {
        _tracker.Seed(1, "(1) A", new[] { Counter }, "Chrome");
        _tracker.Seed(2, "(5) B", new[] { Counter }, "Chrome");
        Assert.Single(Change("(2) A", window: 1, rules: Counter));
        Assert.Empty(Change("(4) B", window: 2, rules: Counter));
    }

    [Fact]
    public void ForgottenWindowStartsFromBaselineAgain()
    {
        _tracker.Seed(1, "(1) A", new[] { Counter }, "Chrome");
        _tracker.Forget(1);
        Assert.Empty(Change("(7) A", rules: Counter));
    }

    [Fact]
    public void InvalidPatternNeverMatches()
    {
        var broken = new TitleRule { Pattern = "(unclosed" };
        Assert.Null(TitleTracker.TryCreate(broken.Pattern, out var error));
        Assert.NotEmpty(error);
        _tracker.Seed(1, "a", new[] { broken }, "x");
        Assert.Empty(Change("(unclosed", "x", rules: broken));
    }
}

public class SourcePriorityTests
{
    private static HostEvent TitleEvent(string app, Priority source) =>
        new("t", app, "Compilación", "Build succeeded", DateTimeOffset.Now, EventOrigin.TitleChange)
        {
            SourcePriority = source,
            SourceRule = "Compilación",
        };

    [Fact]
    public void SourcePriorityBeatsLowerAppRule()
    {
        var decision = PriorityEngine.Evaluate(TitleEvent("Outlook", Priority.High), new AppSettings());
        Assert.Equal(Priority.High, decision.Priority);
        Assert.StartsWith("Regla de título", decision.Reason);
    }

    [Fact]
    public void HigherAppRuleWins() =>
        Assert.Equal(Priority.High, PriorityEngine.Evaluate(TitleEvent("Microsoft Teams", Priority.Low), new AppSettings()).Priority);

    [Fact]
    public void MutedAppStillIgnored() =>
        Assert.Equal(Priority.Ignore, PriorityEngine.Evaluate(TitleEvent("Windows App", Priority.Critical), new AppSettings()).Priority);

    [Fact]
    public void FlashWithoutRuleIsNormal() =>
        Assert.Equal(Priority.Normal, PriorityEngine.Evaluate(
            new HostEvent("f", "Notepad++", "doc.txt", "", DateTimeOffset.Now, EventOrigin.WindowFlash), new AppSettings()).Priority);
}
