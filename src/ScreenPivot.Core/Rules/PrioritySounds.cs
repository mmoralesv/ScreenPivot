using ScreenPivot.Core.Model;

namespace ScreenPivot.Core.Rules;

public sealed record SoundChoice(string Id, string Name);

/// <summary>
/// Sound ids stored in settings: "" (none), "alias:&lt;Windows sound event&gt;" or "file:&lt;path to .wav&gt;".
/// Aliases are the events of the user's Windows sound scheme, so they follow the system theme and volume.
/// </summary>
public static class SoundCatalog
{
    public const string None = "";
    public const string AliasPrefix = "alias:";
    public const string FilePrefix = "file:";

    public static readonly IReadOnlyList<SoundChoice> Builtin = new SoundChoice[]
    {
        new(None, "Ninguno"),
        new("alias:Notification.Default", "Notificación"),
        new("alias:Notification.IM", "Mensaje instantáneo"),
        new("alias:Notification.Mail", "Correo nuevo"),
        new("alias:Notification.Reminder", "Recordatorio"),
        new("alias:Notification.SMS", "SMS"),
        new("alias:SystemAsterisk", "Asterisco"),
        new("alias:SystemExclamation", "Exclamación"),
        new("alias:SystemHand", "Error crítico"),
    };

    public static bool IsFile(string id) => id.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsAlias(string id) => id.StartsWith(AliasPrefix, StringComparison.OrdinalIgnoreCase);

    public static string FromFile(string path) => FilePrefix + path;

    /// <summary>The alias name or file path after the prefix.</summary>
    public static string Target(string id) =>
        IsFile(id) ? id[FilePrefix.Length..] : IsAlias(id) ? id[AliasPrefix.Length..] : "";

    public static string Describe(string id)
    {
        if (IsFile(id)) return $"Archivo: {Path.GetFileName(Target(id))}";
        return Builtin.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))?.Name
               ?? (IsAlias(id) ? Target(id) : "Ninguno");
    }
}

/// <summary>Sound per priority. Defaults keep the original behaviour: only High and Critical make a sound.</summary>
public sealed class PrioritySounds
{
    public string Low { get; set; } = SoundCatalog.None;
    public string Normal { get; set; } = SoundCatalog.None;
    public string High { get; set; } = "alias:SystemAsterisk";
    public string Critical { get; set; } = "alias:SystemHand";

    public string For(Priority p) => p switch
    {
        Priority.Low => Low,
        Priority.Normal => Normal,
        Priority.High => High,
        Priority.Critical => Critical,
        _ => SoundCatalog.None,
    };

    public void Set(Priority p, string id)
    {
        switch (p)
        {
            case Priority.Low: Low = id; break;
            case Priority.Normal: Normal = id; break;
            case Priority.High: High = id; break;
            case Priority.Critical: Critical = id; break;
        }
    }
}
