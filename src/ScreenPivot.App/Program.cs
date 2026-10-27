using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using ScreenPivot.Core.Storage;

namespace ScreenPivot.App;

/// <summary>Single instance: a second launch (Start menu, startup task) just opens the running instance's window.</summary>
public static class Program
{
    private const string InstanceKey = "ScreenPivot.Main";

    /// <summary>"--lang en-US" forces the UI language for this run (screenshots, testing) without saving it.</summary>
    public const string LanguageArgument = "--lang";

    public static readonly string[] SupportedLanguages = { "es-ES", "en-US" };

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // The demo runs beside a real instance instead of redirecting to it.
        bool demo = args.Contains(App.DemoArgument);
        var key = demo ? InstanceKey + ".Demo" : InstanceKey;
        var instance = AppInstance.FindOrRegisterForKey(key);
        if (!instance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Redirect off the STA thread to avoid blocking COM re-entrancy.
            Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }
        instance.Activated += (_, _) => App.Current?.OnRedirectedActivation();

        ApplyLanguage(RequestedLanguage(args, demo));

        Application.Start(_ignored =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(args);
        });
        return 0;
    }

    private static string RequestedLanguage(string[] args, bool demo)
    {
        var i = Array.IndexOf(args, LanguageArgument);
        if (i >= 0 && i + 1 < args.Length) return args[i + 1];
        var directory = demo ? Path.Combine(SettingsStore.DefaultDirectory, "demo") : SettingsStore.DefaultDirectory;
        return new SettingsStore(directory).Load().Language;
    }

    /// <summary>
    /// Must run before any XAML or resource loads. MRT (XAML x:Uid, Loc) and .NET (Core's .resx, date formats)
    /// resolve languages separately, so both are pointed at the same one. Empty = follow Windows.
    /// </summary>
    private static void ApplyLanguage(string language)
    {
        if (!SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase)) language = "";
        try
        {
            // Persisted by Windows for packaged apps, so it must also be cleared when going back to automatic.
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        }
        catch (Exception)
        {
            // Not available in this context; .NET text below still follows the choice.
        }
        if (language.Length == 0) return;

        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
    }
}
