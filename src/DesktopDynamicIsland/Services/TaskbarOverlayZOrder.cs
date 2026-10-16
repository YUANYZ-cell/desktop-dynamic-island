using System.Runtime.InteropServices;

namespace DesktopDynamicIsland.Services;

public static class TaskbarOverlayZOrder
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    public static bool ShouldReassertTopmost(
        bool alwaysOnTop,
        bool isVisible,
        bool suppressedByFullscreen,
        bool hiddenByUser) =>
        alwaysOnTop && isVisible && !suppressedByFullscreen && !hiddenByUser;

    public static bool ReassertTopmost(IntPtr window) =>
        window != IntPtr.Zero && SetWindowPos(
            window,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
