namespace ScreenPivot.Core.Diagnostics;

/// <summary>
/// Small rolling text log in the data folder for troubleshooting. Never write notification titles or bodies here:
/// only origins, app names, priorities and decisions.
/// </summary>
public static class DiagnosticsLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "diagnostics.log");
    }

    public static void Write(string message)
    {
        if (_path is null) return;
        lock (Gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                    File.Move(_path, _path + ".old", overwrite: true);
                File.AppendAllText(_path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Logging must never break the app.
            }
        }
    }
}
