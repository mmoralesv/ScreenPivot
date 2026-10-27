using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace ScreenPivot.App;

/// <summary>Single instance: a second launch (Start menu, startup task) just opens the running instance's window.</summary>
public static class Program
{
    private const string InstanceKey = "ScreenPivot.Main";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // The demo runs beside a real instance instead of redirecting to it.
        var key = args.Contains(App.DemoArgument) ? InstanceKey + ".Demo" : InstanceKey;
        var instance = AppInstance.FindOrRegisterForKey(key);
        if (!instance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Redirect off the STA thread to avoid blocking COM re-entrancy.
            Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }
        instance.Activated += (_, _) => App.Current?.OnRedirectedActivation();

        Application.Start(_ignored =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(args);
        });
        return 0;
    }
}
