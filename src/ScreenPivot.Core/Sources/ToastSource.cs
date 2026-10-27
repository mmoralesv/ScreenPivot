using ScreenPivot.Core.Interop;
using ScreenPivot.Core.Model;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace ScreenPivot.Core.Sources;

public enum ToastAccess
{
    Unknown,
    Allowed,
    Denied,
    Unavailable,
}

/// <summary>
/// Reads toasts from the Notification Center with UserNotificationListener. The NotificationChanged event only
/// works with package identity (Phase 0: 0x80070490 unpackaged), so polling is the fallback; with the event it
/// still polls slowly as a safety net. Only toasts that appear after <see cref="StartAsync"/> are reported.
/// </summary>
public sealed class ToastSource : IDisposable
{
    public static readonly TimeSpan FastPoll = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SafetyPoll = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private UserNotificationListener? _listener;
    private HashSet<uint> _known = new();
    private Timer? _timer;

    public event EventHandler<HostEvent>? EventReceived;

    /// <summary>Errors while polling; the source keeps running.</summary>
    public event EventHandler<Exception>? Failed;

    public ToastAccess Access { get; private set; }

    public bool UsingChangeEvent { get; private set; }

    public bool IsRunning => _timer is not null;

    public ToastAccess CheckAccess()
    {
        try
        {
            _listener ??= UserNotificationListener.Current;
            Access = Map(_listener.GetAccessStatus());
        }
        catch (Exception)
        {
            Access = ToastAccess.Unavailable;
        }
        return Access;
    }

    /// <summary>Shows the system consent prompt; call from the UI thread.</summary>
    public async Task<ToastAccess> RequestAccessAsync()
    {
        try
        {
            _listener ??= UserNotificationListener.Current;
            Access = Map(await _listener.RequestAccessAsync());
        }
        catch (Exception)
        {
            Access = ToastAccess.Unavailable;
        }
        return Access;
    }

    public async Task StartAsync()
    {
        if (IsRunning || CheckAccess() != ToastAccess.Allowed) return;

        _known = (await FetchAsync()).Select(n => n.Id).ToHashSet();

        UsingChangeEvent = false;
        if (Win32.HasPackageIdentity())
        {
            try
            {
                _listener!.NotificationChanged += OnNotificationChanged;
                UsingChangeEvent = true;
            }
            catch (Exception)
            {
                // Same failure as unpackaged; polling covers it.
            }
        }

        var interval = UsingChangeEvent ? SafetyPoll : FastPoll;
        _timer = new Timer(_ => _ = PollAsync(), null, interval, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        if (UsingChangeEvent && _listener is not null)
        {
            try { _listener.NotificationChanged -= OnNotificationChanged; } catch (Exception) { }
            UsingChangeEvent = false;
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind == UserNotificationChangedKind.Added) _ = PollAsync();
    }

    private async Task PollAsync()
    {
        if (!await _pollGate.WaitAsync(0)) return;
        try
        {
            var current = await FetchAsync();
            var fresh = current.Where(n => !_known.Contains(n.Id)).OrderBy(n => n.CreationTime).ToList();
            _known = current.Select(n => n.Id).ToHashSet();
            foreach (var n in fresh)
                EventReceived?.Invoke(this, ToHostEvent(n));
        }
        catch (Exception e)
        {
            Failed?.Invoke(this, e);
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private async Task<IReadOnlyList<UserNotification>> FetchAsync() =>
        await _listener!.GetNotificationsAsync(NotificationKinds.Toast);

    internal static HostEvent ToHostEvent(UserNotification n)
    {
        string app, appId;
        try { app = n.AppInfo?.DisplayInfo?.DisplayName ?? ""; }
        catch (Exception) { app = ""; }
        try { appId = n.AppInfo?.AppUserModelId ?? ""; }
        catch (Exception) { appId = ""; }

        var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements()
            .Select(t => t.Text?.Trim() ?? "")
            .Where(t => t.Length > 0)
            .ToList() ?? new List<string>();

        return new HostEvent(
            $"toast:{n.Id}",
            app, // empty when unknown; shown as HostEvent.DisplayApp
            texts.FirstOrDefault() ?? "",
            string.Join("\n", texts.Skip(1)),
            n.CreationTime,
            EventOrigin.Toast) { AppId = appId };
    }

    private static ToastAccess Map(UserNotificationListenerAccessStatus status) => status switch
    {
        UserNotificationListenerAccessStatus.Allowed => ToastAccess.Allowed,
        UserNotificationListenerAccessStatus.Denied => ToastAccess.Denied,
        _ => ToastAccess.Unknown,
    };

    public void Dispose()
    {
        Stop();
        _pollGate.Dispose();
    }
}
