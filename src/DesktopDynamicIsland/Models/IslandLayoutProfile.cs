namespace DesktopDynamicIsland.Models;

public sealed record IslandLayoutProfile(
    double CollapsedWidth,
    double CollapsedHeight,
    double CollapsedShellHeight,
    double ExpandedWidth,
    double ExpandedHeight,
    TimeSpan ExpandDuration,
    TimeSpan CollapseDuration)
{
    public static IslandLayoutProfile Normal { get; } = new(
        218,
        32,
        26,
        312,
        190,
        TimeSpan.FromMilliseconds(220),
        TimeSpan.FromMilliseconds(180));

    public static IslandLayoutProfile ReducedMotion { get; } = new(
        218,
        32,
        26,
        312,
        190,
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(150));
}
