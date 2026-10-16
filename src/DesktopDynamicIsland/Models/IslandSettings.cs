namespace DesktopDynamicIsland.Models;

public sealed class IslandSettings
{
    // Legacy shared position retained only to migrate prior settings files.
    public string Position { get; set; } = "bottom-right";
    public double X { get; set; } = 1600;
    public double Y { get; set; } = 20;
    public double? CollapsedX { get; set; }
    public double? CollapsedY { get; set; }
    public double? ExpandedX { get; set; }
    public double? ExpandedY { get; set; }
    public bool AutoStart { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public double Opacity { get; set; } = 0.92;
    public string Theme { get; set; } = "dark";
    public int SamplingIntervalSeconds { get; set; } = 5;
    public string CollapsedMetricPair { get; set; } = "cpu-ram";
}
