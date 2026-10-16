using DesktopDynamicIsland.Models;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class IslandLayoutProfileTests
{
    [Fact]
    public void Normal_UsesCompactBoundsAndRestrainedMotion()
    {
        var profile = IslandLayoutProfile.Normal;

        Assert.Equal(218, profile.CollapsedWidth);
        Assert.Equal(32, profile.CollapsedHeight);
        Assert.Equal(26, profile.CollapsedShellHeight);
        Assert.Equal(312, profile.ExpandedWidth);
        Assert.Equal(190, profile.ExpandedHeight);
        Assert.Equal(TimeSpan.FromMilliseconds(220), profile.ExpandDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(180), profile.CollapseDuration);
    }

    [Fact]
    public void ReducedMotion_KeepsGeometryAndShortensTransitions()
    {
        var profile = IslandLayoutProfile.ReducedMotion;

        Assert.Equal(218, profile.CollapsedWidth);
        Assert.Equal(32, profile.CollapsedHeight);
        Assert.Equal(26, profile.CollapsedShellHeight);
        Assert.Equal(312, profile.ExpandedWidth);
        Assert.Equal(190, profile.ExpandedHeight);
        Assert.Equal(TimeSpan.FromMilliseconds(150), profile.ExpandDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(150), profile.CollapseDuration);
    }
}
