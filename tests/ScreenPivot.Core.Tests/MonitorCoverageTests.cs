using ScreenPivot.Core.Detection;
using Xunit;

namespace ScreenPivot.Core.Tests;

public class MonitorCoverageTests
{
    private static readonly Rect Primary = new(0, 0, 1920, 1080);
    private static readonly Rect PrimaryWork = new(0, 0, 1920, 1032);
    private static readonly Rect Secondary = new(1920, 0, 4480, 1440);
    private static readonly Rect SecondaryWork = new(1920, 0, 4480, 1392);

    [Fact]
    public void FullScreenWindowCoversMonitor() =>
        Assert.Equal(CoverageKind.FullScreen, MonitorCoverage.Classify(Primary, PrimaryWork, Primary));

    [Fact]
    public void MaximizedWindowLeavesTaskbarVisible() =>
        Assert.Equal(CoverageKind.Maximized, MonitorCoverage.Classify(Primary, PrimaryWork, PrimaryWork));

    [Fact]
    public void RestoredWindowDoesNotCover() =>
        Assert.Equal(CoverageKind.None, MonitorCoverage.Classify(Primary, PrimaryWork, new Rect(100, 100, 1200, 800)));

    [Fact]
    public void WindowOnOtherMonitorDoesNotCover() =>
        Assert.Equal(CoverageKind.None, MonitorCoverage.Classify(Primary, PrimaryWork, Secondary));

    [Fact]
    public void SpanningWindowCoversBothMonitors()
    {
        var spanning = new Rect(0, 0, 4480, 1440);
        Assert.Equal(CoverageKind.FullScreen, MonitorCoverage.Classify(Primary, PrimaryWork, spanning));
        Assert.Equal(CoverageKind.FullScreen, MonitorCoverage.Classify(Secondary, SecondaryWork, spanning));
    }

    [Fact]
    public void OneWindowPerMonitorPicksBestMatch()
    {
        var windows = new[] { new Rect(100, 100, 800, 600), Secondary };
        Assert.Equal(CoverageKind.None, MonitorCoverage.Classify(Primary, PrimaryWork, windows));
        Assert.Equal(CoverageKind.FullScreen, MonitorCoverage.Classify(Secondary, SecondaryWork, windows));
    }

    [Fact]
    public void SmallGapsWithinThresholdStillCount() =>
        Assert.Equal(CoverageKind.FullScreen, MonitorCoverage.Classify(Primary, PrimaryWork, new Rect(0, 8, 1920, 1080)));

    [Fact]
    public void EmptyMonitorIsNeverCovered() =>
        Assert.Equal(0, MonitorCoverage.Ratio(new Rect(0, 0, 0, 0), Primary));
}
