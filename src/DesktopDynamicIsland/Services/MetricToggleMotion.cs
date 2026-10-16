namespace DesktopDynamicIsland.Services;

public static class MetricToggleMotion
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(240);
    public static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(140);
    public const double RotationDegrees = 160;
    public const double RestingAngleDegrees = 0;
    public const double MinimumOpacity = 0.35;
}
