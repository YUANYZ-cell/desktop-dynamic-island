using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace DesktopDynamicIsland.Services;

public enum CollapsedPlacementMode
{
    TaskbarOverlay,
    WorkAreaBottomRightFallback
}

public enum TaskbarEdge
{
    Unknown,
    Left,
    Top,
    Right,
    Bottom
}

public sealed record DesktopPlacementSnapshot(
    string MonitorDeviceName,
    Rect MonitorRectPx,
    Rect WorkAreaRectPx,
    Rect WorkAreaRectDip,
    Rect TaskbarRectPx,
    TaskbarEdge TaskbarEdge,
    double DpiScaleX,
    double DpiScaleY,
    Point CollapsedTaskbarTargetDip,
    Point CollapsedFallbackTargetDip,
    CollapsedPlacementMode ActiveCollapsedPlacementMode,
    Point ActiveCollapsedTargetDip,
    Point ExpandedTargetDip,
    string PlacementReason);

public sealed class DesktopPlacementService
{
    private const uint MonitorDefaultToNearest = 2;

    public DesktopPlacementSnapshot Capture(Window window, Size collapsedSizeDip, Size expandedSizeDip)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitorHandle = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (monitorHandle == IntPtr.Zero || !GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            throw new InvalidOperationException("Unable to resolve the current monitor.");
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
        var monitorRectPx = monitorInfo.Monitor.ToRect();
        var workAreaRectPx = monitorInfo.WorkArea.ToRect();
        var workAreaTopLeftDip = ScreenPxToDip(window, workAreaRectPx.TopLeft);
        var workAreaBottomRightDip = ScreenPxToDip(window, workAreaRectPx.BottomRight);
        var workAreaRectDip = new Rect(workAreaTopLeftDip, workAreaBottomRightDip);

        var taskbar = FindTaskbarForMonitor(monitorRectPx);
        var taskbarRectPx = taskbar?.RectanglePx ?? Rect.Empty;
        var taskbarEdge = taskbar is null ? TaskbarEdge.Unknown : DetermineTaskbarEdge(taskbarRectPx, monitorRectPx, workAreaRectPx);
        var collapsedFallbackTargetDip = TaskbarAnchorCalculator.GetWorkAreaBottomRightTarget(
            workAreaRectDip,
            collapsedSizeDip.Width,
            collapsedSizeDip.Height);
        var expandedTargetDip = TaskbarAnchorCalculator.GetWorkAreaBottomRightTarget(
            workAreaRectDip,
            expandedSizeDip.Width,
            expandedSizeDip.Height);

        var collapsedTaskbarTargetDip = default(Point);
        var activeMode = CollapsedPlacementMode.WorkAreaBottomRightFallback;
        var activeTarget = collapsedFallbackTargetDip;
        var reason = taskbar is null
            ? "No taskbar window matched the current monitor."
            : taskbarEdge != TaskbarEdge.Bottom
                ? $"Taskbar edge is {taskbarEdge}; only bottom taskbars are eligible for overlay placement."
                : string.Empty;

        if (taskbar is not null && taskbarEdge == TaskbarEdge.Bottom)
        {
            var collapsedWidthPx = collapsedSizeDip.Width * scaleX;
            var collapsedHeightPx = collapsedSizeDip.Height * scaleY;
            if (collapsedHeightPx <= taskbar.RectanglePx.Height)
            {
                var taskbarTargetPx = TaskbarAnchorCalculator.GetStableBottomTaskbarTarget(
                    taskbar.RectanglePx,
                    collapsedWidthPx,
                    collapsedHeightPx,
                    scaleX);
                collapsedTaskbarTargetDip = ScreenPxToDip(window, taskbarTargetPx);
                activeMode = CollapsedPlacementMode.TaskbarOverlay;
                activeTarget = collapsedTaskbarTargetDip;
                reason = "Pinned bottom-taskbar left slot.";
            }
            else
            {
                reason = "Collapsed window height exceeds taskbar height.";
            }
        }

        return new DesktopPlacementSnapshot(
            string.IsNullOrWhiteSpace(monitorInfo.DeviceName) ? "unknown" : monitorInfo.DeviceName,
            monitorRectPx,
            workAreaRectPx,
            workAreaRectDip,
            taskbarRectPx,
            taskbarEdge,
            scaleX,
            scaleY,
            collapsedTaskbarTargetDip,
            collapsedFallbackTargetDip,
            activeMode,
            activeTarget,
            expandedTargetDip,
            reason);
    }

    private static Point ScreenPxToDip(Window window, Point screenPointPx)
    {
        var relativeDip = window.PointFromScreen(screenPointPx);
        return new Point(window.Left + relativeDip.X, window.Top + relativeDip.Y);
    }

    private static TaskbarSnapshot? FindTaskbarForMonitor(Rect monitorRectPx)
    {
        TaskbarSnapshot? match = null;
        EnumWindows((window, _) =>
        {
            var className = GetWindowClass(window);
            if (className is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd") ||
                !TryGetRect(window, out var rectangle) ||
                !rectangle.IntersectsWith(monitorRectPx))
            {
                return true;
            }

            match = new TaskbarSnapshot(rectangle);
            return false;
        }, IntPtr.Zero);
        return match;
    }

    private static TaskbarEdge DetermineTaskbarEdge(Rect taskbar, Rect monitor, Rect workArea)
    {
        const double tolerance = 2;
        if (Math.Abs(taskbar.Top - workArea.Bottom) <= tolerance && Math.Abs(taskbar.Bottom - monitor.Bottom) <= tolerance)
        {
            return TaskbarEdge.Bottom;
        }

        if (Math.Abs(taskbar.Bottom - workArea.Top) <= tolerance && Math.Abs(taskbar.Top - monitor.Top) <= tolerance)
        {
            return TaskbarEdge.Top;
        }

        if (Math.Abs(taskbar.Left - workArea.Right) <= tolerance && Math.Abs(taskbar.Right - monitor.Right) <= tolerance)
        {
            return TaskbarEdge.Right;
        }

        if (Math.Abs(taskbar.Right - workArea.Left) <= tolerance && Math.Abs(taskbar.Left - monitor.Left) <= tolerance)
        {
            return TaskbarEdge.Left;
        }

        return TaskbarEdge.Unknown;
    }

    private static bool TryGetRect(IntPtr window, out Rect rect)
    {
        if (GetWindowRect(window, out var native))
        {
            rect = native.ToRect();
            return rect.Width > 0 && rect.Height > 0;
        }

        rect = Rect.Empty;
        return false;
    }

    private static string GetWindowClass(IntPtr window)
    {
        var builder = new StringBuilder(128);
        _ = GetClassName(window, builder, builder.Capacity);
        return builder.ToString();
    }

    private sealed record TaskbarSnapshot(Rect RectanglePx);

    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx information);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }
}
