using ScreenPivot.Core.Model;
using ScreenPivot.Core.Rules;
using ScreenPivot.Core.Storage;
using Xunit;

namespace ScreenPivot.Core.Tests;

public sealed class SoundTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ScreenPivotTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultsKeepOnlyHighAndCritical()
    {
        var sounds = new PrioritySounds();
        Assert.Equal(SoundCatalog.None, sounds.For(Priority.Low));
        Assert.Equal(SoundCatalog.None, sounds.For(Priority.Normal));
        Assert.NotEqual(SoundCatalog.None, sounds.For(Priority.High));
        Assert.NotEqual(SoundCatalog.None, sounds.For(Priority.Critical));
        Assert.Equal(SoundCatalog.None, sounds.For(Priority.Ignore));
    }

    [Fact]
    public void SetAndGetPerPriority()
    {
        var sounds = new PrioritySounds();
        sounds.Set(Priority.Normal, "alias:Notification.Mail");
        sounds.Set(Priority.Ignore, "alias:SystemHand"); // no slot, ignored
        Assert.Equal("alias:Notification.Mail", sounds.For(Priority.Normal));
        Assert.Equal(SoundCatalog.None, sounds.For(Priority.Ignore));
    }

    [Theory]
    [InlineData("", "Ninguno")]
    [InlineData("alias:Notification.IM", "Mensaje instantáneo")]
    [InlineData("alias:Custom.Event", "Custom.Event")]
    [InlineData(@"file:C:\Sonidos\alerta.wav", "Archivo: alerta.wav")]
    public void Describe(string id, string expected) => Assert.Equal(expected, SoundCatalog.Describe(id));

    [Fact]
    public void FileIdsRoundTrip()
    {
        var id = SoundCatalog.FromFile(@"C:\Sonidos\alerta.wav");
        Assert.True(SoundCatalog.IsFile(id));
        Assert.False(SoundCatalog.IsAlias(id));
        Assert.Equal(@"C:\Sonidos\alerta.wav", SoundCatalog.Target(id));
        Assert.Equal("SystemHand", SoundCatalog.Target("alias:SystemHand"));
        Assert.Equal("", SoundCatalog.Target(""));
    }

    [Fact]
    public void SoundsPersistAndOldSettingsGetDefaults()
    {
        var store = new SettingsStore(_dir);
        var settings = new AppSettings();
        settings.Sounds.Normal = "alias:Notification.Default";
        store.Save(settings);
        Assert.Equal("alias:Notification.Default", store.Load().Sounds.Normal);

        // Settings saved before sounds existed.
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{ "SoundEnabled": true }""");
        var old = store.Load();
        Assert.Equal(new PrioritySounds().Critical, old.Sounds.Critical);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
