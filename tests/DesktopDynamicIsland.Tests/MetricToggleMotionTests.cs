using DesktopDynamicIsland.Services;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class MetricToggleMotionTests
{
    [Fact]
    public void MotionProfile_IsVisibleButRestrained()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(240), MetricToggleMotion.Duration);
        Assert.Equal(160, MetricToggleMotion.RotationDegrees);
        Assert.Equal(0, MetricToggleMotion.RestingAngleDegrees);
        Assert.Equal(0.35, MetricToggleMotion.MinimumOpacity);
    }
}
