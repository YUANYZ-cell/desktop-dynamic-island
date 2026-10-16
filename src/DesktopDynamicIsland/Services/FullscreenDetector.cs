using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopDynamicIsland.Services;

public sealed class FullscreenDetector
{
    private const uint MonitorDefaultToNearest = 2;
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };
    private static readonly HashSet<string> RemoteClientProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "mstsc",
        "msrdc",
        "WindowsApp",
        "RemoteDesktop"
    };
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "vivaldi"
    };
    private IntPtr _lastForegroundWindow;
    private bool _lastForegroundWasRemoteClient;

    public bool IsForegroundFullscreen(IntPtr ownWindow)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero ||
            foreground == ownWindow ||
            foreground == GetShellWindow() ||
            !IsWindowVisible(foreground) ||
            IsIconic(foreground))
        {
            return false;
        }

        var className = new StringBuilder(128);
        _ = GetClassName(foreground, className, className.Capacity);
        if (ShellClasses.Contains(className.ToString()))
        {
            return false;
        }

        if (!GetWindowRect(foreground, out var windowRect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        return CoversMonitor(windowRect.ToRectangle(), info.Monitor.ToRectangle());
    }

    public static bool CoversMonitor(Rectangle window, Rectangle monitor, int tolerance = 2)
    {
        return window.Left <= monitor.Left + tolerance &&
               window.Top <= monitor.Top + tolerance &&
               window.Right >= monitor.Right - tolerance &&
               window.Bottom >= monitor.Bottom - tolerance;
    }

    public bool IsForegroundRemoteClient(IntPtr ownWindow)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == ownWindow)
        {
            return false;
        }

        if (foreground == _lastForegroundWindow)
        {
            return _lastForegroundWasRemoteClient;
        }

        _lastForegroundWindow = foreground;
        _lastForegroundWasRemoteClient = false;
        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            _lastForegroundWasRemoteClient = IsRemoteClientProcessName(process.ProcessName);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        return _lastForegroundWasRemoteClient;
    }

    public static bool IsRemoteClientProcessName(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) && RemoteClientProcesses.Contains(processName);

    public bool IsForegroundBrowser(IntPtr ownWindow)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == ownWindow)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return IsBrowserProcessName(process.ProcessName);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool IsBrowserProcessName(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) && BrowserProcesses.Contains(processName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo information);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
