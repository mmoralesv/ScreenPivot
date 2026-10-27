using System.Security.Cryptography;
using System.Text.Json;
using ScreenPivot.Core.Model;

namespace ScreenPivot.Core.Storage;

/// <summary>
/// Recent accepted events. Persisted encrypted with DPAPI (current user) because it holds third-party
/// notification content; entries older than the retention window are pruned on load and on add.
/// </summary>
public sealed class DigestStore
{
    private readonly object _gate = new();
    private readonly List<HostEvent> _events = new();
    private readonly string? _path;
    private TimeSpan _retention;

    public DigestStore(string? directory, TimeSpan retention)
    {
        _retention = retention;
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "digest.bin");
        Load();
    }

    public event EventHandler? Changed;

    public TimeSpan Retention
    {
        get => _retention;
        set { lock (_gate) { _retention = value; Prune(DateTimeOffset.Now); } }
    }

    public void Add(HostEvent e)
    {
        lock (_gate)
        {
            _events.Add(e);
            Prune(e.Timestamp);
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<HostEvent> Since(DateTimeOffset since)
    {
        lock (_gate) return _events.Where(e => e.Timestamp >= since).ToList();
    }

    public IReadOnlyList<HostEvent> All()
    {
        lock (_gate) return _events.ToList();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Prune(DateTimeOffset now) => _events.RemoveAll(e => now - e.Timestamp > _retention);

    private void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            var json = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            var events = JsonSerializer.Deserialize<List<HostEvent>>(json, SettingsStore.JsonOptions);
            if (events is null) return;
            _events.AddRange(events);
            Prune(DateTimeOffset.Now);
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            File.Delete(_path); // unreadable history is not worth keeping
        }
    }

    private void Save()
    {
        if (_path is null) return;
        var json = JsonSerializer.SerializeToUtf8Bytes(_events, SettingsStore.JsonOptions);
        File.WriteAllBytes(_path, ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser));
    }
}
