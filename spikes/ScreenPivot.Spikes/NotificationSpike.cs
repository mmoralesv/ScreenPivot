using System.Diagnostics;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace ScreenPivot.Spikes;

/// <summary>
/// Checks whether UserNotificationListener works from this process (packaged vs unpackaged), lists the toasts in the
/// Notification Center and watches for new ones via the NotificationChanged event and via polling.
/// Notification text is redacted unless --show-text is passed.
/// </summary>
public static class NotificationSpike
{
    public static void Run(int seconds, bool request, bool showText)
    {
        var package = Native.CurrentPackageFullName();
        Console.WriteLine($"Identidad de paquete: {package ?? "NINGUNA (proceso sin empaquetar)"}");

        UserNotificationListener listener;
        try
        {
            listener = UserNotificationListener.Current;
        }
        catch (Exception e)
        {
            Console.WriteLine($"UserNotificationListener.Current falló: {e.GetType().Name} 0x{e.HResult:X8} {e.Message}");
            return;
        }

        UserNotificationListenerAccessStatus status;
        try
        {
            status = listener.GetAccessStatus();
            Console.WriteLine($"GetAccessStatus: {status}");
            if (status != UserNotificationListenerAccessStatus.Allowed && request)
            {
                status = listener.RequestAccessAsync().AsTask().GetAwaiter().GetResult();
                Console.WriteLine($"RequestAccessAsync: {status}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Acceso falló: {e.GetType().Name} 0x{e.HResult:X8} {e.Message}");
            return;
        }
        if (status != UserNotificationListenerAccessStatus.Allowed)
        {
            Console.WriteLine("Sin acceso. Ejecuta con --request o habilítalo en Configuración > Privacidad > Notificaciones.");
            return;
        }

        var seen = new HashSet<uint>();
        var initial = Fetch(listener);
        Console.WriteLine($"Toasts actualmente en el Centro de notificaciones: {initial.Count}");
        foreach (var n in initial)
        {
            seen.Add(n.Id);
            Print("existente", n, showText);
        }

        int eventsReceived = 0;
        bool eventSupported;
        try
        {
            listener.NotificationChanged += (_, args) =>
            {
                Interlocked.Increment(ref eventsReceived);
                Console.WriteLine($"  [evento] {args.ChangeKind} id={args.UserNotificationId}");
            };
            eventSupported = true;
            Console.WriteLine("Suscripción a NotificationChanged: OK");
        }
        catch (Exception e)
        {
            eventSupported = false;
            Console.WriteLine($"Suscripción a NotificationChanged: NO soportada ({e.GetType().Name} 0x{e.HResult:X8}) -> se usará polling");
        }

        Console.WriteLine($"Observando {seconds}s. Genera notificaciones (correo, Teams, toast de prueba)...");
        var sw = Stopwatch.StartNew();
        int newByPolling = 0;
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(2000);
            foreach (var n in Fetch(listener).Where(n => seen.Add(n.Id)))
            {
                newByPolling++;
                Print($"nuevo +{sw.Elapsed:mm\\:ss}", n, showText);
            }
        }

        Console.WriteLine();
        Console.WriteLine("Resultado:");
        Console.WriteLine($"  nuevos detectados por polling: {newByPolling}");
        Console.WriteLine($"  eventos NotificationChanged: {(eventSupported ? eventsReceived.ToString() : "n/a")}");
        Console.WriteLine($"  estado de notificaciones del shell al terminar: {Native.NotificationState()}");
    }

    private static IReadOnlyList<UserNotification> Fetch(UserNotificationListener listener) =>
        listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().GetAwaiter().GetResult();

    private static void Print(string label, UserNotification n, bool showText)
    {
        string app;
        try { app = n.AppInfo?.DisplayInfo?.DisplayName ?? "(desconocida)"; }
        catch { app = "(sin AppInfo)"; }

        var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements()
                        .Select(t => t.Text ?? "").ToList() ?? new List<string>();
        var content = showText
            ? string.Join(" | ", texts)
            : string.Join(" | ", texts.Select(t => $"<{t.Length} chars>"));
        Console.WriteLine($"  [{label}] id={n.Id} app='{app}' creado={n.CreationTime.LocalDateTime:HH:mm:ss} texto: {content}");
    }
}
