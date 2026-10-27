using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;
using Xunit;

namespace ScreenPivot.Core.Tests;

public class PriorityEngineTests
{
    private static HostEvent Event(string app, string title, string body = "") =>
        new("id", app, title, body, DateTimeOffset.Now, EventOrigin.Toast);

    [Fact]
    public void MutedAppIsIgnored() =>
        Assert.Equal(Priority.Ignore, PriorityEngine.Evaluate(Event("Windows App", "Conectado"), new AppSettings()).Priority);

    [Fact]
    public void VipSenderInTitleIsCritical()
    {
        var settings = new AppSettings { VipSenders = { "Ana Pérez" } };
        var decision = PriorityEngine.Evaluate(Event("Outlook", "ana pérez", "Revisión del contrato"), settings);
        Assert.Equal(Priority.Critical, decision.Priority);
        Assert.Contains("VIP", decision.Reason);
    }

    [Fact]
    public void CriticalKeywordInBodyIsCritical() =>
        Assert.Equal(Priority.Critical, PriorityEngine.Evaluate(Event("Slack", "#ops", "Incidente en producción"), new AppSettings()).Priority);

    [Fact]
    public void TeamsCallBeatsTeamsMessageRule() =>
        Assert.Equal(Priority.Critical, PriorityEngine.Evaluate(Event("Microsoft Teams", "Luis te está llamando"), new AppSettings()).Priority);

    [Fact]
    public void TeamsMessageIsHigh() =>
        Assert.Equal(Priority.High, PriorityEngine.Evaluate(Event("Microsoft Teams", "Luis", "¿Tienes un minuto?"), new AppSettings()).Priority);

    [Fact]
    public void UnknownAppDefaultsToNormal()
    {
        var decision = PriorityEngine.Evaluate(Event("Google Chrome", "Descarga completa"), new AppSettings());
        Assert.Equal(Priority.Normal, decision.Priority);
        Assert.Equal("Sin regla", decision.Reason);
    }

    [Fact]
    public void DisabledRulesAreSkipped()
    {
        var settings = new AppSettings();
        settings.Rules.ForEach(r => r.Enabled = false);
        Assert.Equal(Priority.Normal, PriorityEngine.Evaluate(Event("Microsoft Teams", "Luis", "hola"), settings).Priority);
    }

    [Fact]
    public void BlankEntriesNeverMatch()
    {
        var settings = new AppSettings { VipSenders = { " " }, CriticalKeywords = { "" }, MutedApps = { "" } };
        Assert.Equal(Priority.Normal, PriorityEngine.Evaluate(Event("Chrome", "x"), settings).Priority);
    }
}
