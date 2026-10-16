using DesktopDynamicIsland.Services;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class MetricMathTests
{
    [Fact]
    public void Smooth_UsesCurrentValueForFirstSample()
    {
        Assert.Equal(1200, MetricMath.Smooth(null, 1200, 0.35));
    }

    [Fact]
    public void Smooth_AppliesExponentialMovingAverage()
    {
        Assert.Equal(1350, MetricMath.Smooth(1000, 2000, 0.35));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(0)]
    [InlineData(1.1)]
    public void Smooth_RejectsInvalidWeight(double weight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MetricMath.Smooth(10, 20, weight));
    }
}
