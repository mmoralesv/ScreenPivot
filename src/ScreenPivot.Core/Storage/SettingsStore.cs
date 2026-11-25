using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenPivot.Core.Rules;

namespace ScreenPivot.Core.Storage;

public sealed class SettingsStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SettingsStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
    }

    private static string? _defaultDirectory;

    /// <summary>
    /// Unpackaged: %LOCALAPPDATA%\ScreenPivot. Packaged: the package's own LocalState, so the Store app and
    /// ScreenPivot Dev keep separate settings and history (MSIX gives every package the same virtualized view of
    /// %LOCALAPPDATA%\ScreenPivot once that folder exists). The first packaged run imports the old files from there.
    /// </summary>
    public static string DefaultDirectory => _defaultDirectory ??= ResolveDefaultDirectory();

    private static string LegacyDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenPivot");

    private static string ResolveDefaultDirectory()
    {
        string packageFolder;
        try
        {
            packageFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return LegacyDirectory; // no package identity
        }
        ImportLegacy(LegacyDirectory, packageFolder);
        return packageFolder;
    }

    /// <summary>
    /// Copies settings and history from <paramref name="from"/> when <paramref name="to"/> has no settings yet.
    /// The log stays behind. Never overwrites.
    /// </summary>
    /// <returns>True when settings were imported.</returns>
    internal static bool ImportLegacy(string from, string to)
    {
        var settings = Path.Combine(from, "settings.json");
        if (File.Exists(Path.Combine(to, "settings.json")) || !File.Exists(settings)) return false;
        try
        {
            Directory.CreateDirectory(to);
            File.Copy(settings, Path.Combine(to, "settings.json"));
            var digest = Path.Combine(from, "digest.bin");
            if (File.Exists(digest)) File.Copy(digest, Path.Combine(to, "digest.bin"), overwrite: false);
            return true;
        }
        catch (IOException)
        {
            return false; // e.g. both packages starting at once; defaults are fine
        }
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            File.Copy(_path, _path + ".corrupt", overwrite: true);
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }
}
