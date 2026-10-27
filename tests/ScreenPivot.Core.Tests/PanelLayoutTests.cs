using ScreenPivot.Core.Detection;
using ScreenPivot.Core.Overlay;
using ScreenPivot.Core.Rules;
using Xunit;

namespace ScreenPivot.Core.Tests;

public class PanelLayoutTests
{
    private static readonly Rect Area = new(1920, 0, 3840, 1080);

    [Fact]
    public void RightIsFullHeightAgainstTheRightEdge() =>
        Assert.Equal(new Rect(3840 - 525, 0, 3840, 1080), PanelLayout.Compute(Area, PanelEdge.Right, 1.25));

    [Fact]
    public void LeftStartsAtTheMonitorLeft() =>
        Assert.Equal(new Rect(1920, 0, 1920 + 420, 1080), PanelLayout.Compute(Area, PanelEdge.Left, 1.0));

    [Fact]
    public void TopIsCentredAndCapped()
    {
        var r = PanelLayout.Compute(Area, PanelEdge.Top, 1.0);
        Assert.Equal(980, r.Width);
        Assert.Equal(440, r.Height);
        Assert.Equal(0, r.Top);
        Assert.Equal(1920 + (1920 - 980) / 2, r.Left);
    }

    [Fact]
    public void BottomSitsOnTheBottomEdge()
    {
        var workArea = new Rect(0, 0, 1920, 1032);
        var r = PanelLayout.Compute(workArea, PanelEdge.Bottom, 1.0);
        Assert.Equal(1032, r.Bottom);
        Assert.Equal(1032 - 440, r.Top);
    }

    [Fact]
    public void NeverLargerThanTheArea()
    {
        var small = new Rect(0, 0, 300, 200);
        foreach (var edge in Enum.GetValues<PanelEdge>())
        {
            var r = PanelLayout.Compute(small, edge, 2.0);
            Assert.True(r.Width <= 300 && r.Height <= 200, edge.ToString());
            Assert.True(r.Left >= 0 && r.Top >= 0, edge.ToString());
        }
    }
}

public class MonitorPickerTests
{
    private static readonly MonitorDesc[] Monitors =
    {
        new(0, "A", new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1032), Primary: true),
        new(1, "B", new Rect(1920, 0, 3840, 1080), new Rect(1920, 0, 3840, 1032), Primary: false),
    };

    [Fact]
    public void ExplicitIndexWins() =>
        Assert.Equal(1, MonitorPicker.Pick(Monitors, new[] { CoverageKind.FullScreen, CoverageKind.None }, 1)!.Index);

    [Fact]
    public void AutoPrefersTheCoveredMonitor() =>
        Assert.Equal(1, MonitorPicker.Pick(Monitors, new[] { CoverageKind.None, CoverageKind.FullScreen }, -1)!.Index);

    [Fact]
    public void FallsBackToPrimary()
    {
        Assert.Equal(0, MonitorPicker.Pick(Monitors, Array.Empty<CoverageKind>(), -1)!.Index);
        Assert.Equal(0, MonitorPicker.Pick(Monitors, Array.Empty<CoverageKind>(), 7)!.Index);
    }

    [Fact]
    public void AtPointFindsTheMonitorUnderTheCursor()
    {
        Assert.Equal(1, MonitorPicker.AtPoint(Monitors, 2500, 300)!.Index);
        Assert.Equal(0, MonitorPicker.AtPoint(Monitors, 0, 0)!.Index);
        Assert.Equal(1, MonitorPicker.AtPoint(Monitors, 1920, 10)!.Index); // right/bottom edges are exclusive
        Assert.Null(MonitorPicker.AtPoint(Monitors, -5, 10));
    }

    [Fact]
    public void NoMonitors() => Assert.Null(MonitorPicker.Pick(Array.Empty<MonitorDesc>(), Array.Empty<CoverageKind>(), -1));
}
