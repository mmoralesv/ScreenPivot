using System.Globalization;
using System.Resources;
using ScreenPivot.Core.Model;

namespace ScreenPivot.Core.Localization;

/// <summary>
/// Localized text produced by Core (Resources/Strings*.resx). Uses the current UI culture unless one is passed,
/// which keeps tests deterministic regardless of the machine's language. Spanish is the neutral fallback.
/// </summary>
public static class CoreText
{
    private static readonly ResourceManager Resources = new("ScreenPivot.Core.Resources.Strings", typeof(CoreText).Assembly);

    public static string Get(string key, CultureInfo? culture = null) =>
        Resources.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, CultureInfo? culture, params object[] args)
    {
        culture ??= CultureInfo.CurrentUICulture;
        return string.Format(culture, Get(key, culture), args);
    }

    /// <summary>"1 aviso" / "3 avisos" style counts; <paramref name="noun"/> is Alert, Critical or High.</summary>
    public static string Count(string noun, int n, CultureInfo? culture = null) =>
        n == 1 ? Get($"Count_{noun}_One", culture) : Format($"Count_{noun}_Many", culture, n);

    public static string Priority(Priority p, CultureInfo? culture = null) => Get($"Priority_{p}", culture);

    public static string Reason(ReasonKind kind, string detail, CultureInfo? culture = null) => kind switch
    {
        ReasonKind.Muted => Format("Reason_Muted", culture, detail),
        ReasonKind.Vip => Format("Reason_Vip", culture, detail),
        ReasonKind.Keyword => Format("Reason_Keyword", culture, detail),
        ReasonKind.Rule => Format("Reason_Rule", culture, detail),
        ReasonKind.TitleRule => Format("Reason_TitleRule", culture, detail),
        ReasonKind.Test => Get("Reason_Test", culture),
        ReasonKind.NoRule => Get("Reason_NoRule", culture),
        _ => "",
    };
}
