using System.Globalization;
using System.Xml.Linq;
using ScreenPivot.Core.Digest;
using ScreenPivot.Core.Localization;
using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;
using Xunit;

namespace ScreenPivot.Core.Tests;

public class LocalizationTests
{
    private static readonly CultureInfo Es = new("es-ES"), En = new("en-US");

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ScreenPivot.sln"))) dir = dir.Parent;
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    /// <summary>Keys and non-empty values of a .resx or .resw file (same XML format).</summary>
    private static Dictionary<string, string> Load(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    public static TheoryData<string, string> ResourcePairs => new()
    {
        { @"src\ScreenPivot.Core\Resources\Strings.resx", @"src\ScreenPivot.Core\Resources\Strings.en.resx" },
        { @"src\ScreenPivot.App\Strings\es-ES\Resources.resw", @"src\ScreenPivot.App\Strings\en-US\Resources.resw" },
    };

    [Theory]
    [MemberData(nameof(ResourcePairs))]
    public void SpanishAndEnglishHaveTheSameKeys(string spanish, string english)
    {
        var es = Load(RepoFile(spanish.Split('\\')));
        var en = Load(RepoFile(english.Split('\\')));
        Assert.Empty(es.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(es.Keys));
        Assert.All(es.Concat(en), kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"{kv.Key} is empty"));
    }

    [Theory]
    [MemberData(nameof(ResourcePairs))]
    public void PlaceholdersMatchBetweenLanguages(string spanish, string english)
    {
        var es = Load(RepoFile(spanish.Split('\\')));
        var en = Load(RepoFile(english.Split('\\')));
        static string Holes(string s) => string.Join(",", System.Text.RegularExpressions.Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Order());
        Assert.All(es, kv => Assert.Equal(Holes(kv.Value), Holes(en[kv.Key])));
    }

    [Fact]
    public void ReasonsAreFormattedPerLanguage()
    {
        Assert.Equal("Palabra clave: urgente", CoreText.Reason(ReasonKind.Keyword, "urgente", Es));
        Assert.Equal("Keyword: urgente", CoreText.Reason(ReasonKind.Keyword, "urgente", En));
        Assert.Equal("No rule", CoreText.Reason(ReasonKind.NoRule, "", En));
        Assert.Equal("", CoreText.Reason(ReasonKind.None, "", En));
    }

    [Fact]
    public void OtherLanguagesFallBackToSpanish() =>
        Assert.Equal("Sin avisos nuevos.", CoreText.Get("Summary_None", new CultureInfo("fr-FR")));

    [Fact]
    public void EnglishRegionalCulturesUseEnglish() =>
        Assert.Equal("No new alerts.", CoreText.Get("Summary_None", new CultureInfo("en-GB")));

    [Fact]
    public void HeadlineMentionsMoreThanThreeAppsInEnglish()
    {
        var events = new[] { "A", "B", "C", "D" }.Select(a => new HostEvent(a, a, "t", "", DateTimeOffset.Now, EventOrigin.Toast)).ToList();
        Assert.Equal("4 alerts: A (1), B (1), C (1) and 1 more.", RulesSummarizer.Summarize(events, En).Headline);
    }

    [Fact]
    public void SoundNamesFollowTheLanguage()
    {
        Assert.Equal("Critical stop", SoundCatalog.Describe("alias:SystemHand", En));
        Assert.Equal("File: alerta.wav", SoundCatalog.Describe(@"file:C:\x\alerta.wav", En));
        Assert.Equal(SoundCatalog.Builtin(Es).Count, SoundCatalog.Builtin(En).Count);
    }

    [Fact]
    public void FlashAndUnknownAppAreNotStoredAsText()
    {
        var flash = new HostEvent("f", "", "doc.txt", "", DateTimeOffset.Now, EventOrigin.WindowFlash);
        Assert.Equal("", flash.Body);
        Assert.Equal(CoreText.Get("Event_FlashBody"), flash.DisplayBody);
        Assert.Equal(CoreText.Get("Event_UnknownApp"), flash.DisplayApp);
        var toast = new HostEvent("t", "Outlook", "x", "", DateTimeOffset.Now, EventOrigin.Toast);
        Assert.Equal("", toast.DisplayBody);
    }

    [Fact]
    public void ReasonIsNotSerialized()
    {
        var e = new HostEvent("1", "Outlook", "t", "b", DateTimeOffset.Now, EventOrigin.Toast) { ReasonKind = ReasonKind.Keyword, ReasonDetail = "urgente" };
        var json = System.Text.Json.JsonSerializer.Serialize(e);
        Assert.Contains("\"ReasonKind\"", json);
        Assert.DoesNotContain("\"Reason\":", json);
        Assert.DoesNotContain("DisplayBody", json);
    }
}
