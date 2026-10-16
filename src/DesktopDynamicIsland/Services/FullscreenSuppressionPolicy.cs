namespace DesktopDynamicIsland.Services;

public static class FullscreenSuppressionPolicy
{
    public static bool ShouldSuppress(
        bool foregroundCoversMonitor,
        bool currentSessionIsRemote,
        bool foregroundIsRemoteClient,
        bool foregroundIsBrowser)
    {
        _ = currentSessionIsRemote;
        _ = foregroundIsRemoteClient;
        return foregroundCoversMonitor && !foregroundIsBrowser;
    }
}
