using System.Windows;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using Point = System.Windows.Point;

namespace DesktopDynamicIsland.Services;

/// <summary>
/// Calculates the collapsed island position from the primary display work area.
/// Windows removes a bottom taskbar from this work area, so its bottom edge is
/// the taskbar's top edge regardless of taskbar transparency.
/// </summary>
public static class TaskbarAnchorCalculator
{
    public const double EdgeMarginDip = 12;
    public const double TaskbarSlotInsetPx = 12;
    public const double MinimumScreenInset = 8;
    public const double StableLeftSlotOffsetDip = 208;

    public static Point GetWorkAreaBottomRightTarget(Rect workArea, double windowWidth, double windowHeight)
    {
        var x = Math.Max(workArea.Left + EdgeMarginDip, workArea.Right - windowWidth - EdgeMarginDip);
        var y = Math.Max(workArea.Top + EdgeMarginDip, workArea.Bottom - windowHeight - EdgeMarginDip);
        return new Point(x, y);
    }

    public static Point GetBottomRightAnchor(Rect workArea, double windowWidth, double windowHeight) =>
        GetWorkAreaBottomRightTarget(workArea, windowWidth, windowHeight);

    public static Point GetStableBottomTaskbarTarget(
        Rect taskbarRectPx,
        double windowWidthPx,
        double windowHeightPx,
        double dpiScaleX)
    {
        var minimumX = taskbarRectPx.Left + TaskbarSlotInsetPx;
        var maximumX = Math.Max(minimumX, taskbarRectPx.Right - windowWidthPx - TaskbarSlotInsetPx);
        var preferredX = taskbarRectPx.Left + (StableLeftSlotOffsetDip * Math.Max(1, dpiScaleX));

        return new Point(
            Math.Clamp(preferredX, minimumX, maximumX),
            taskbarRectPx.Top + ((taskbarRectPx.Height - windowHeightPx) / 2));
    }

    public static bool TryGetBottomTaskbarTarget(
        Rect taskbarRectPx,
        IReadOnlyList<Rect> occupiedRectsPx,
        double windowWidthPx,
        double windowHeightPx,
        out Point targetPx,
        out string failureReason)
    {
        targetPx = default;
        failureReason = string.Empty;

        if (taskbarRectPx.Width <= 0 || taskbarRectPx.Height <= 0)
        {
            failureReason = "Taskbar rectangle is invalid.";
            return false;
        }

        if (windowHeightPx > taskbarRectPx.Height)
        {
            failureReason = "Collapsed window height exceeds taskbar height.";
            return false;
        }

        var occupiedIntervals = occupiedRectsPx
            .Where(rect => rect.Width > 0 && rect.Height > 0 && rect.IntersectsWith(taskbarRectPx))
            .Select(rect => (
                Left: Math.Max(taskbarRectPx.Left, rect.Left),
                Right: Math.Min(taskbarRectPx.Right, rect.Right)))
            .Where(interval => interval.Right > interval.Left)
            .OrderBy(interval => interval.Left)
            .ToArray();

        if (occupiedIntervals.Length < 2)
        {
            failureReason = "Taskbar controls do not expose a reliable application-to-tray gap.";
            return false;
        }

        var freeIntervals = new List<(double Left, double Right)>();
        var cursor = taskbarRectPx.Left;
        foreach (var interval in occupiedIntervals)
        {
            if (interval.Left > cursor)
            {
                freeIntervals.Add((cursor, interval.Left));
            }

            cursor = Math.Max(cursor, interval.Right);
        }

        if (cursor < taskbarRectPx.Right)
        {
            freeIntervals.Add((cursor, taskbarRectPx.Right));
        }

        var requiredWidth = windowWidthPx + (TaskbarSlotInsetPx * 2);
        var slot = freeIntervals
            .Where(interval => interval.Right - interval.Left >= requiredWidth)
            .OrderBy(interval => interval.Left)
            .FirstOrDefault();

        if (slot.Right <= slot.Left)
        {
            failureReason = "No taskbar gap can fit the collapsed window without covering controls.";
            return false;
        }

        targetPx = new Point(
            slot.Left + ((slot.Right - slot.Left - windowWidthPx) / 2),
            taskbarRectPx.Top + ((taskbarRectPx.Height - windowHeightPx) / 2));
        return true;
    }

    public static Point ClampToWorkArea(Rect workArea, double windowWidth, double windowHeight, Point position) => new(
        Math.Clamp(position.X, workArea.Left + MinimumScreenInset, Math.Max(workArea.Left + MinimumScreenInset, workArea.Right - windowWidth - MinimumScreenInset)),
        Math.Clamp(position.Y, workArea.Top + MinimumScreenInset, Math.Max(workArea.Top + MinimumScreenInset, workArea.Bottom - windowHeight - MinimumScreenInset)));

    public static DrawingPoint ClampPixelRectangleToWorkArea(
        DrawingRectangle windowRectangle,
        DrawingRectangle workArea,
        int inset)
    {
        var minimumX = workArea.Left + inset;
        var minimumY = workArea.Top + inset;
        var maximumX = Math.Max(minimumX, workArea.Right - windowRectangle.Width - inset);
        var maximumY = Math.Max(minimumY, workArea.Bottom - windowRectangle.Height - inset);

        return new DrawingPoint(
            Math.Clamp(windowRectangle.Left, minimumX, maximumX),
            Math.Clamp(windowRectangle.Top, minimumY, maximumY));
    }
}
