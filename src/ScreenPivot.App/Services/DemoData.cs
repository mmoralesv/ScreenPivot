using System.Globalization;
using ScreenPivot.Core.Localization;
using ScreenPivot.Core.Model;

namespace ScreenPivot.App.Services;

/// <summary>
/// Fictional events for <c>--demo</c>, in the UI language: names and content are invented, never read from the
/// machine. Used for Store screenshots and by certification testers.
/// </summary>
internal static class DemoData
{
    private static bool English => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

    public static string VipSender => "Laura Gómez";

    public static IReadOnlyList<HostEvent> Session(DateTimeOffset now)
    {
        HostEvent E(int minutesAgo, string app, string title, string body, Priority priority, ReasonKind kind, string detail = "",
            EventOrigin origin = EventOrigin.Toast) =>
            new($"demo:{minutesAgo}", app, title, body, now.AddMinutes(-minutesAgo), origin)
            {
                Priority = priority, ReasonKind = kind, ReasonDetail = detail,
            };

        string Rule(string key) => CoreText.Get(key);

        return English
            ? new[]
            {
                E(42, "Outlook", "Quarterly review — agenda", "Attached is the agenda for Thursday's meeting.", Priority.Normal, ReasonKind.Rule, Rule("Rule_OutlookMail")),
                E(37, "Microsoft Teams", "Platform team", "Can someone review PR #482 before noon?", Priority.High, ReasonKind.Rule, Rule("Rule_TeamsMessages")),
                E(31, "Outlook", "Supplier invoice", "Document waiting for approval.", Priority.Normal, ReasonKind.Rule, Rule("Rule_OutlookMail")),
                E(24, "CI Pipeline", Rule("TitleRule_Build"), "release/2.4 — Build succeeded", Priority.High, ReasonKind.TitleRule, Rule("TitleRule_Build"), EventOrigin.TitleChange),
                E(18, "Microsoft Teams", "Carlos Ruiz", "I uploaded the deck to the shared folder.", Priority.High, ReasonKind.Rule, Rule("Rule_TeamsMessages")),
                E(12, "Ticket tracker", "Ticket #1093 assigned", "", Priority.Normal, ReasonKind.NoRule, origin: EventOrigin.WindowFlash),
                E(6, "Outlook", "Urgent: staging access down", "The support team needs confirmation.", Priority.Critical, ReasonKind.Keyword, "urgent"),
                E(2, "Microsoft Teams", $"{VipSender} is calling you", "", Priority.Critical, ReasonKind.Rule, Rule("Rule_TeamsCalls")),
            }
            : new[]
            {
                E(42, "Outlook", "Revisión trimestral — agenda", "Adjunto la agenda para la reunión del jueves.", Priority.Normal, ReasonKind.Rule, Rule("Rule_OutlookMail")),
                E(37, "Microsoft Teams", "Equipo de Plataforma", "¿Alguien puede revisar el PR #482 antes de las 12?", Priority.High, ReasonKind.Rule, Rule("Rule_TeamsMessages")),
                E(31, "Outlook", "Factura de proveedor", "Documento pendiente de aprobación.", Priority.Normal, ReasonKind.Rule, Rule("Rule_OutlookMail")),
                E(24, "Pipeline CI", Rule("TitleRule_Build"), "release/2.4 — Build succeeded", Priority.High, ReasonKind.TitleRule, Rule("TitleRule_Build"), EventOrigin.TitleChange),
                E(18, "Microsoft Teams", "Carlos Ruiz", "Subí la presentación a la carpeta compartida.", Priority.High, ReasonKind.Rule, Rule("Rule_TeamsMessages")),
                E(12, "Gestor de tickets", "Ticket #1093 asignado", "", Priority.Normal, ReasonKind.NoRule, origin: EventOrigin.WindowFlash),
                E(6, "Outlook", "Urgente: acceso caído en staging", "El equipo de soporte necesita confirmación.", Priority.Critical, ReasonKind.Keyword, "urgente"),
                E(2, "Microsoft Teams", $"{VipSender} te está llamando", "", Priority.Critical, ReasonKind.Rule, Rule("Rule_TeamsCalls")),
            };
    }

    /// <summary>The realistic banner "Test banner" shows in demo mode (a Teams call from the sample session).</summary>
    public static HostEvent CallBanner(DateTimeOffset now) =>
        new("test", "Microsoft Teams", English ? $"{VipSender} is calling you" : $"{VipSender} te está llamando",
            English ? "Incoming Teams call" : "Llamada entrante de Teams", now, EventOrigin.Test)
        {
            Priority = Priority.Critical, ReasonKind = ReasonKind.Rule, ReasonDetail = CoreText.Get("Rule_TeamsCalls"),
        };
}
