using System.Windows;
using DesktopDynamicIsland.Services;
using Xunit;
using DrawingRectangle = System.Drawing.Rectangle;

namespace DesktopDynamicIsland.Tests;

public sealed class TaskbarAnchorCalculatorTests
{
    [Fact]
    public void WorkAreaBottomRightTarget_UsesTwelveDipMargins()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var anchor = TaskbarAnchorCalculator.GetWorkAreaBottomRightTarget(workArea, 196, 32);

        Assert.Equal(1712, anchor.X);
        Assert.Equal(996, anchor.Y);
    }

    [Fact]
    public void WorkAreaBottomRightTarget_PreservesNegativeMonitorOrigin()
    {
        var anchor = TaskbarAnchorCalculator.GetWorkAreaBottomRightTarget(
            new Rect(-1920, 0, 1920, 1040),
            196,
            32);

        Assert.Equal(-208, anchor.X);
        Assert.Equal(996, anchor.Y);
    }

    [Fact]
    public void BottomTaskbarTarget_PrefersGapBetweenWeatherAndTaskList()
    {
        var taskbar = new Rect(0, 1528, 2560, 72);
        var occupied = new[]
        {
            new Rect(0, 1528, 306, 72),
            new Rect(749, 1528, 799, 72),
            new Rect(2170, 1528, 390, 72)
        };

        var success = TaskbarAnchorCalculator.TryGetBottomTaskbarTarget(
            taskbar,
            occupied,
            windowWidthPx: 294,
            windowHeightPx: 48,
            out var target,
            out var reason);

        Assert.True(success, reason);
        Assert.Equal(380.5, target.X);
        Assert.Equal(1540, target.Y);
    }

    [Fact]
    public void StableBottomTaskbarTarget_UsesFixedLeftSlot()
    {
        var target = TaskbarAnchorCalculator.GetStableBottomTaskbarTarget(
            new Rect(0, 1528, 2560, 72),
            windowWidthPx: 318,
            windowHeightPx: 48,
            dpiScaleX: 1.5);

        Assert.Equal(312, target.X);
        Assert.Equal(1540, target.Y);
    }

    [Fact]
    public void BottomTaskbarTarget_RejectsWindowTallerThanTaskbar()
    {
        var success = TaskbarAnchorCalculator.TryGetBottomTaskbarTarget(
            new Rect(0, 1019, 1707, 48),
            new[] { new Rect(499, 1019, 533, 48), new Rect(1445, 1019, 262, 48) },
            windowWidthPx: 294,
            windowHeightPx: 60,
            out _,
            out var reason);

        Assert.False(success);
        Assert.Contains("height", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandedWindowClamp_PreservesPositionInsideSecondaryWorkArea()
    {
        var position = TaskbarAnchorCalculator.ClampPixelRectangleToWorkArea(
            new DrawingRectangle(2140, 180, 468, 285),
            new DrawingRectangle(1920, 0, 2560, 1400),
            12);

        Assert.Equal(new System.Drawing.Point(2140, 180), position);
    }

    [Fact]
    public void ExpandedWindowClamp_RecoversOffscreenPositionInsideNearestWorkArea()
    {
        var position = TaskbarAnchorCalculator.ClampPixelRectangleToWorkArea(
            new DrawingRectangle(4380, 1320, 468, 285),
            new DrawingRectangle(1920, 0, 2560, 1400),
            12);

        Assert.Equal(new System.Drawing.Point(4000, 1103), position);
    }
}
