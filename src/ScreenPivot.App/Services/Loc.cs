using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace ScreenPivot.App.Services;

/// <summary>
/// Strings from Strings\{es-ES,en-US}\Resources.resw for code (XAML uses x:Uid). MRT picks the language set at
/// startup (Program.ApplyLanguage), so this matches what the XAML shows.
/// </summary>
internal static class Loc
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key)
    {
        var value = Loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), args);
}
