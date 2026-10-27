using ScreenPivot.Core.Overlay;
using ScreenPivot.Core.Switching;
using Xunit;

namespace ScreenPivot.Core.Tests;

public class AppWindowMatcherTests
{
    private static WindowCandidate W(int id, string title, string process = "", string windowId = "", string processId = "") =>
        new(new IntPtr(id), windowId, processId, process, title);

    [Fact]
    public void ExactWindowIdBeatsTitleMatch()
    {
        var candidates = new[]
        {
            W(1, "Correo - Outlook - Google Chrome", "chrome", "Chrome"),
            W(2, "Bandeja de entrada - Outlook", "olk", processId: "Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows"),
        };
        var best = AppWindowMatcher.Best(candidates, "Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows", "Outlook");
        Assert.Equal(new IntPtr(2), best!.Hwnd);
    }

    [Fact]
    public void WindowIdIsPreferredOverProcessId()
    {
        var candidates = new[] { W(1, "a", processId: "App!X"), W(2, "b", windowId: "App!X") };
        Assert.Equal(new IntPtr(2), AppWindowMatcher.Best(candidates, "App!X", "")!.Hwnd);
    }

    [Fact]
    public void ExePathIdMatchesProcess() =>
        Assert.Equal(AppWindowMatcher.Heuristic,
            AppWindowMatcher.Score(W(1, "Consola", "powershell"), @"{1AC14E77}\WindowsPowerShell\v1.0\powershell.exe", "Windows PowerShell"));

    [Fact]
    public void TitleFallbackUsesDisplayName() =>
        Assert.Equal(new IntPtr(1), AppWindowMatcher.Best(new[] { W(1, "Chat | Ana | Microsoft Teams") }, "", "Microsoft Teams")!.Hwnd);

    [Fact]
    public void FirstInZOrderWinsTies()
    {
        var candidates = new[] { W(1, "Uno - Outlook"), W(2, "Dos - Outlook") };
        Assert.Equal(new IntPtr(1), AppWindowMatcher.Best(candidates, "", "Outlook")!.Hwnd);
    }

    [Fact]
    public void NoMatchReturnsNull()
    {
        Assert.Null(AppWindowMatcher.Best(new[] { W(1, "Bloc de notas", "notepad") }, "Slack", "Slack"));
        Assert.Null(AppWindowMatcher.Best(new[] { W(1, "ab") }, "", "ab")); // too short to match titles
    }
}

public class OverlayPlannerTests
{
    private static OverlayInputs In(bool remote = true, bool host = false, bool parked = false, bool banner = false,
        bool test = false, int pending = 0, bool paused = false, bool quick = true) =>
        new(remote, host, parked, banner, test, pending, paused, quick);

    [Fact] public void HiddenOnHostWithoutSession() => Assert.Equal(OverlayView.Hidden, OverlayPlanner.Decide(In(remote: false, host: true)));
    [Fact] public void BannerWinsInRemote() => Assert.Equal(OverlayView.Banner, OverlayPlanner.Decide(In(banner: true, pending: 3)));
    [Fact] public void TestBannerAlwaysShows() => Assert.Equal(OverlayView.Banner, OverlayPlanner.Decide(In(remote: false, test: true)));
    [Fact] public void TickerWhenPending() => Assert.Equal(OverlayView.Ticker, OverlayPlanner.Decide(In(pending: 2)));
    [Fact] public void QuickAccessWhenNothingPending() => Assert.Equal(OverlayView.QuickAccess, OverlayPlanner.Decide(In()));
    [Fact] public void QuickAccessCanBeTurnedOff() => Assert.Equal(OverlayView.Hidden, OverlayPlanner.Decide(In(quick: false)));
    [Fact] public void ReturnWhenHostInFront() => Assert.Equal(OverlayView.ReturnToRemote, OverlayPlanner.Decide(In(host: true, pending: 2)));
    [Fact] public void ReturnWhenParked() => Assert.Equal(OverlayView.ReturnToRemote, OverlayPlanner.Decide(In(remote: false, parked: true)));
    [Fact] public void ReturnStillAvailableWhenPaused() => Assert.Equal(OverlayView.ReturnToRemote, OverlayPlanner.Decide(In(host: true, paused: true)));
    [Fact] public void OpenPanelHidesOverlay() => Assert.Equal(OverlayView.Hidden, OverlayPlanner.Decide(In(banner: true, pending: 2) with { PanelOpen = true }));
    [Fact] public void TestBannerShowsEvenWithPanelOpen() => Assert.Equal(OverlayView.Banner, OverlayPlanner.Decide(In(test: true) with { PanelOpen = true }));
    [Fact] public void PausedHidesTickerAndBanner() => Assert.Equal(OverlayView.QuickAccess, OverlayPlanner.Decide(In(banner: true, pending: 2, paused: true)));
}
