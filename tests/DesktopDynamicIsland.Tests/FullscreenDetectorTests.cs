using System.Drawing;
using DesktopDynamicIsland.Services;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class FullscreenDetectorTests
{
    [Fact]
    public void SuppressionGate_IgnoresSingleTransientFullscreenDetection()
    {
        var gate = new FullscreenSuppressionGate(requiredConsecutiveDetections: 2);

        Assert.False(gate.Update(true));
        Assert.False(gate.Update(false));
    }

    [Fact]
    public void SuppressionGate_HidesAfterTwoConsecutiveFullscreenDetections()
    {
        var gate = new FullscreenSuppressionGate(requiredConsecutiveDetections: 2);

        Assert.False(gate.Update(true));
        Assert.True(gate.Update(true));
    }

    [Fact]
    public void SuppressionGate_RestoresImmediatelyAfterFullscreenEnds()
    {
        var gate = new FullscreenSuppressionGate(requiredConsecutiveDetections: 2);
        gate.Update(true);
        gate.Update(true);

        Assert.False(gate.Update(false));
    }

    [Theory]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, false, false, false, false)]
    public void SuppressionPolicy_HidesFullscreenGamesButNotBrowsers(
        bool foregroundCoversMonitor,
        bool currentSessionIsRemote,
        bool foregroundIsRemoteClient,
        bool foregroundIsBrowser,
        bool expected)
    {
        Assert.Equal(
            expected,
            FullscreenSuppressionPolicy.ShouldSuppress(
                foregroundCoversMonitor,
                currentSessionIsRemote,
                foregroundIsRemoteClient,
                foregroundIsBrowser));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("msedge")]
    [InlineData("firefox")]
    [InlineData("brave")]
    [InlineData("opera")]
    public void IsBrowserProcessName_AcceptsKnownBrowsers(string processName)
    {
        Assert.True(FullscreenDetector.IsBrowserProcessName(processName));
    }

    [Theory]
    [InlineData("game")]
    [InlineData("steam_app_12345")]
    [InlineData("LeagueClient")]
    [InlineData("")]
    public void IsBrowserProcessName_RejectsGamesAndEmptyNames(string processName)
    {
        Assert.False(FullscreenDetector.IsBrowserProcessName(processName));
    }

    [Fact]
    public void CoversMonitor_AllowsTwoPixelTolerance()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var window = Rectangle.FromLTRB(1, 0, 1919, 1080);

        Assert.True(FullscreenDetector.CoversMonitor(window, monitor));
    }

    [Fact]
    public void CoversMonitor_RejectsMaximizedWindowThatLeavesTaskbar()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var window = Rectangle.FromLTRB(0, 0, 1920, 1040);

        Assert.False(FullscreenDetector.CoversMonitor(window, monitor));
    }

    [Theory]
    [InlineData("mstsc")]
    [InlineData("msrdc")]
    [InlineData("WindowsApp")]
    [InlineData("RemoteDesktop")]
    public void IsRemoteClientProcessName_AcceptsKnownClients(string processName)
    {
        Assert.True(FullscreenDetector.IsRemoteClientProcessName(processName));
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("devenv")]
    [InlineData("")]
    public void IsRemoteClientProcessName_RejectsNormalApplications(string processName)
    {
        Assert.False(FullscreenDetector.IsRemoteClientProcessName(processName));
    }

    [Fact]
    public void RemoteSessionDetector_CurrentSessionQueryDoesNotThrow()
    {
        var detector = new RemoteSessionDetector();

        var exception = Record.Exception(() => detector.IsCurrentSessionRemote(DateTimeOffset.Now));

        Assert.Null(exception);
    }
}
