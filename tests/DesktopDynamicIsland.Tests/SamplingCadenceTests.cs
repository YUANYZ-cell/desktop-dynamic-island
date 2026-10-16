using DesktopDynamicIsland.Models;
using DesktopDynamicIsland.Services;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class SamplingCadenceTests
{
    [Theory]
    [InlineData(IslandDisplayState.Collapsed, 1500)]
    [InlineData(IslandDisplayState.Collapsing, 750)]
    [InlineData(IslandDisplayState.Expanding, 750)]
    [InlineData(IslandDisplayState.Expanded, 750)]
    public void DisplayState_SelectsExpectedSamplingInterval(IslandDisplayState state, int milliseconds)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), SamplingCadence.ForState(state));
    }
}
