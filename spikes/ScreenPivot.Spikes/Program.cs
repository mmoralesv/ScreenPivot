using ScreenPivot.Spikes;

Console.OutputEncoding = System.Text.Encoding.UTF8;
// Physical pixels everywhere so window and monitor rects are comparable across mixed-DPI setups.
Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

var command = args.FirstOrDefault()?.ToLowerInvariant();
int Int(string name, int fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback;
}
bool Flag(string name) => args.Contains(name);

switch (command)
{
    case "detect":
        if (Flag("--watch")) RemoteSessionDetector.Watch(Int("--seconds", 60));
        else RemoteSessionDetector.Print(RemoteSessionDetector.Detect());
        break;

    case "overlay":
        var monitor = Int("--monitor", -1);
        OverlaySpike.Run(Int("--seconds", 30), Int("--delay", 5), monitor >= 0 ? monitor : null, !Flag("--no-reassert"));
        break;

    case "notifications":
        NotificationSpike.Run(Int("--seconds", 60), Flag("--request"), Flag("--show-text"));
        break;

    case "activity":
        ActivitySpike.Watch(Int("--seconds", 60));
        break;

    case "fake-app":
        ActivitySpike.FakeApp(Int("--delay", 3));
        break;

    case "apps":
        // Windows the "Abrir" action can switch to, with the IDs used to match notifications.
        foreach (var w in ScreenPivot.Core.Switching.WindowSwitcher.GetCandidates())
            Console.WriteLine($"0x{w.Hwnd:X} {w.ProcessName,-22} win='{w.WindowAppId}' proc='{w.ProcessAppId}'");
        break;

    default:
        Console.WriteLine("""
            ScreenPivot — spikes de Fase 0

              detect                       Foto instantánea: monitores, ventanas de Windows App/RDP, cobertura.
              detect --watch [--seconds N]  Observa cambios con WinEvent hooks (por defecto 60s).
              overlay [--seconds N] [--delay N] [--monitor I] [--no-reassert]
                                           Banner topmost click-through; mide si queda encima del Cloud PC.
              notifications [--seconds N] [--request] [--show-text]
                                           Prueba UserNotificationListener (texto oculto por defecto).
            """);
        break;
}
