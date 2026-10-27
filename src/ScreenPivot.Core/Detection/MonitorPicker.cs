namespace ScreenPivot.Core.Detection;

public static class MonitorPicker
{
    /// <summary>
    /// The monitor at <paramref name="index"/> when valid; otherwise the first one covered by the remote session,
    /// then the primary one. Null only when there are no monitors.
    /// </summary>
    public static MonitorDesc? Pick(IReadOnlyList<MonitorDesc> monitors, IReadOnlyList<CoverageKind> coverage, int index)
    {
        if (monitors.Count == 0) return null;
        if (index >= 0 && index < monitors.Count) return monitors[index];
        return monitors.FirstOrDefault(m => m.Index < coverage.Count && coverage[m.Index] != CoverageKind.None)
               ?? monitors.FirstOrDefault(m => m.Primary)
               ?? monitors[0];
    }

    /// <summary>The monitor containing the point (e.g. the cursor), or null when it is off every monitor.</summary>
    public static MonitorDesc? AtPoint(IReadOnlyList<MonitorDesc> monitors, int x, int y) =>
        monitors.FirstOrDefault(m => x >= m.Bounds.Left && x < m.Bounds.Right && y >= m.Bounds.Top && y < m.Bounds.Bottom);
}
