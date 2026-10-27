namespace ScreenPivot.Core.Switching;

/// <summary>A switchable top-level window on the host.</summary>
public sealed record WindowCandidate(IntPtr Hwnd, string WindowAppId, string ProcessAppId, string ProcessName, string Title);

/// <summary>
/// Picks the window that best belongs to a notification's app. Candidates come in z-order (most recent first),
/// so on equal scores the most recently used window wins.
/// </summary>
public static class AppWindowMatcher
{
    public const int ExactWindowId = 3;
    public const int ExactProcessId = 2;
    public const int Heuristic = 1;

    public static int Score(WindowCandidate w, string appId, string displayName)
    {
        if (appId.Length > 0)
        {
            if (string.Equals(w.WindowAppId, appId, StringComparison.OrdinalIgnoreCase)) return ExactWindowId;
            if (string.Equals(w.ProcessAppId, appId, StringComparison.OrdinalIgnoreCase)) return ExactProcessId;
            // Unpackaged apps often use the exe path or name as their ID (e.g. "...\\powershell.exe", "Chrome").
            if (w.ProcessName.Length > 0 &&
                (appId.EndsWith($"\\{w.ProcessName}.exe", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(appId, w.ProcessName, StringComparison.OrdinalIgnoreCase)))
                return Heuristic;
        }
        // Window titles usually end with the app name: "Bandeja de entrada - Outlook", "Chat | Microsoft Teams".
        if (displayName.Trim().Length >= 3 && w.Title.Contains(displayName.Trim(), StringComparison.CurrentCultureIgnoreCase))
            return Heuristic;
        return 0;
    }

    public static WindowCandidate? Best(IEnumerable<WindowCandidate> candidates, string appId, string displayName)
    {
        WindowCandidate? best = null;
        int bestScore = 0;
        foreach (var w in candidates)
        {
            var score = Score(w, appId, displayName);
            if (score > bestScore)
            {
                best = w;
                bestScore = score;
            }
        }
        return best;
    }
}
