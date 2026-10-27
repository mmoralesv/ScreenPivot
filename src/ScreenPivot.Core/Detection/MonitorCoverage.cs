namespace ScreenPivot.Core.Detection;

public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public long Area => Width <= 0 || Height <= 0 ? 0 : (long)Width * Height;

    public Rect Intersect(Rect other) => new(
        Math.Max(Left, other.Left), Math.Max(Top, other.Top),
        Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));

    public override string ToString() => $"({Left},{Top}) {Width}x{Height}";
}

public enum CoverageKind
{
    None,
    /// <summary>Covers the work area but the taskbar is still visible.</summary>
    Maximized,
    /// <summary>Covers the whole monitor, taskbar included.</summary>
    FullScreen,
}

public static class MonitorCoverage
{
    public const double DefaultThreshold = 0.98;

    public static double Ratio(Rect target, Rect window) =>
        target.Area == 0 ? 0 : (double)target.Intersect(window).Area / target.Area;

    public static CoverageKind Classify(Rect monitor, Rect workArea, Rect window, double threshold = DefaultThreshold)
    {
        if (Ratio(monitor, window) >= threshold) return CoverageKind.FullScreen;
        if (Ratio(workArea, window) >= threshold) return CoverageKind.Maximized;
        return CoverageKind.None;
    }

    /// <summary>Best coverage any single window achieves on the monitor (a session may use one window per monitor).</summary>
    public static CoverageKind Classify(Rect monitor, Rect workArea, IEnumerable<Rect> windows, double threshold = DefaultThreshold)
    {
        var best = CoverageKind.None;
        foreach (var w in windows)
        {
            var kind = Classify(monitor, workArea, w, threshold);
            if (kind > best) best = kind;
        }
        return best;
    }
}
