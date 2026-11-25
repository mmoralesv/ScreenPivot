using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;
using ScreenPivot.Core.Storage;
using Xunit;

namespace ScreenPivot.Core.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ScreenPivotTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ImportLegacyCopiesSettingsAndHistoryOnce()
    {
        var legacy = Path.Combine(_dir, "legacy");
        var package = Path.Combine(_dir, "package");
        new SettingsStore(legacy).Save(new AppSettings { VipSenders = { "Ana" } });
        File.WriteAllText(Path.Combine(legacy, "digest.bin"), "x");
        File.WriteAllText(Path.Combine(legacy, "diagnostics.log"), "log");

        Assert.True(SettingsStore.ImportLegacy(legacy, package));
        Assert.Equal(new[] { "Ana" }, new SettingsStore(package).Load().VipSenders);
        Assert.True(File.Exists(Path.Combine(package, "digest.bin")));
        Assert.False(File.Exists(Path.Combine(package, "diagnostics.log")));

        // Later runs keep the package's own settings.
        new SettingsStore(package).Save(new AppSettings { VipSenders = { "Luis" } });
        Assert.False(SettingsStore.ImportLegacy(legacy, package));
        Assert.Equal(new[] { "Luis" }, new SettingsStore(package).Load().VipSenders);
    }

    [Fact]
    public void ImportLegacyWithoutOldSettingsDoesNothing() =>
        Assert.False(SettingsStore.ImportLegacy(Path.Combine(_dir, "none"), Path.Combine(_dir, "package")));

    [Fact]
    public void SettingsRoundTrip()
    {
        var store = new SettingsStore(_dir);
        var settings = new AppSettings { VipSenders = { "Ana" }, Corner = OverlayCorner.TopLeft, Paused = true };
        store.Save(settings);

        var loaded = store.Load();
        Assert.Equal(new[] { "Ana" }, loaded.VipSenders);
        Assert.Equal(OverlayCorner.TopLeft, loaded.Corner);
        Assert.True(loaded.Paused);
        Assert.Equal(settings.Rules.Count, loaded.Rules.Count);
    }

    [Fact]
    public void CorruptSettingsFallBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        Assert.NotEmpty(new SettingsStore(_dir).Load().Rules);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json.corrupt")));
    }

    [Fact]
    public void DigestIsEncryptedAndReloaded()
    {
        var e = new HostEvent("1", "Outlook", "Secreto", "Cuerpo", DateTimeOffset.Now, EventOrigin.Toast) { Priority = Priority.High };
        new DigestStore(_dir, TimeSpan.FromHours(1)).Add(e);

        var raw = File.ReadAllText(Path.Combine(_dir, "digest.bin"));
        Assert.DoesNotContain("Secreto", raw);

        var reloaded = new DigestStore(_dir, TimeSpan.FromHours(1)).All();
        Assert.Equal("Secreto", Assert.Single(reloaded).Title);
        Assert.Equal(Priority.High, reloaded[0].Priority);
    }

    [Fact]
    public void ExpiredEntriesArePrunedOnLoad()
    {
        var old = new HostEvent("1", "Outlook", "Viejo", "", DateTimeOffset.Now.AddHours(-3), EventOrigin.Toast);
        new DigestStore(_dir, TimeSpan.FromHours(24)).Add(old);
        Assert.Empty(new DigestStore(_dir, TimeSpan.FromHours(1)).All());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
