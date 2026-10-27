using ScreenPivot.Core.Model;

namespace ScreenPivot.App.Services;

/// <summary>Fictional events for <c>--demo</c>: names and content are invented, never read from the machine.</summary>
internal static class DemoData
{
    public static IReadOnlyList<HostEvent> Session(DateTimeOffset now)
    {
        HostEvent E(int minutesAgo, string app, string title, string body, Priority priority, string reason, EventOrigin origin = EventOrigin.Toast) =>
            new($"demo:{minutesAgo}", app, title, body, now.AddMinutes(-minutesAgo), origin) { Priority = priority, Reason = reason };

        return new[]
        {
            E(42, "Outlook", "Revisión trimestral — agenda", "Adjunto la agenda para la reunión del jueves.", Priority.Normal, "Regla: Correo de Outlook"),
            E(37, "Microsoft Teams", "Equipo de Plataforma", "¿Alguien puede revisar el PR #482 antes de las 12?", Priority.High, "Regla: Mensajes de Teams"),
            E(31, "Outlook", "Factura de proveedor", "Documento pendiente de aprobación.", Priority.Normal, "Regla: Correo de Outlook"),
            E(24, "Pipeline CI", "Compilación o despliegue terminado", "release/2.4 — Build succeeded", Priority.High,
                "Regla de título: Compilación o despliegue terminado", EventOrigin.TitleChange),
            E(18, "Microsoft Teams", "Carlos Ruiz", "Subí la presentación a la carpeta compartida.", Priority.High, "Regla: Mensajes de Teams"),
            E(12, "Gestor de tickets", "Ticket #1093 asignado", "Pide tu atención (parpadea en la barra de tareas).", Priority.Normal, "Sin regla", EventOrigin.WindowFlash),
            E(6, "Outlook", "Urgente: acceso caído en staging", "El equipo de soporte necesita confirmación.", Priority.Critical, "Palabra clave: urgente"),
            E(2, "Microsoft Teams", "Laura Gómez te está llamando", "", Priority.Critical, "Regla: Llamadas de Teams"),
        };
    }
}
