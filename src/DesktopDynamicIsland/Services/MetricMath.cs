namespace DesktopDynamicIsland.Services;

public static class MetricMath
{
    public static double Smooth(double? previous, double current, double currentWeight)
    {
        if (currentWeight is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(currentWeight));
        }

        return previous is double value
            ? value + ((current - value) * currentWeight)
            : current;
    }
}
