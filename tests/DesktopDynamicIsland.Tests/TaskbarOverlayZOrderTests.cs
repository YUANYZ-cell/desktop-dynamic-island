using DesktopDynamicIsland.Services;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class TaskbarOverlayZOrderTests
{
    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    public void ShouldReassertTopmost_OnlyForVisibleUnsuppressedOverlay(
        bool alwaysOnTop,
        bool isVisible,
        bool suppressedByFullscreen,
        bool hiddenByUser,
        bool expected)
    {
        Assert.Equal(
            expected,
            TaskbarOverlayZOrder.ShouldReassertTopmost(
                alwaysOnTop,
                isVisible,
                suppressedByFullscreen,
                hiddenByUser));
    }
}
