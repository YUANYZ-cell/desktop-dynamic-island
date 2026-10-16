using DesktopDynamicIsland.Models;

namespace DesktopDynamicIsland.Services;

public static class SamplingCadence
{
    public static readonly TimeSpan CollapsedInterval = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan ExpandedInterval = TimeSpan.FromMilliseconds(750);

    public static TimeSpan ForState(IslandDisplayState state) => state switch
    {
        IslandDisplayState.Expanding or IslandDisplayState.Expanded or IslandDisplayState.Collapsing => ExpandedInterval,
        _ => CollapsedInterval
    };
}
