using System.Runtime.InteropServices;

namespace DesktopDynamicIsland.Services;

public sealed class RemoteSessionDetector
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);
    private DateTimeOffset _lastRead = DateTimeOffset.MinValue;
    private bool _cached;

    public bool IsCurrentSessionRemote(DateTimeOffset now)
    {
        if (now - _lastRead < RefreshInterval)
        {
            return _cached;
        }

        _lastRead = now;
        if (!WtsQuerySessionInformation(
                IntPtr.Zero,
                uint.MaxValue,
                29,
                out var buffer,
                out var bytesReturned))
        {
            _cached = false;
            return false;
        }

        try
        {
            _cached = bytesReturned >= sizeof(int) && Marshal.ReadInt32(buffer) != 0;
            return _cached;
        }
        finally
        {
            WtsFreeMemory(buffer);
        }
    }

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WtsQuerySessionInformation(
        IntPtr server,
        uint sessionId,
        int infoClass,
        out IntPtr buffer,
        out int bytesReturned);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSFreeMemory")]
    private static extern void WtsFreeMemory(IntPtr memory);
}
