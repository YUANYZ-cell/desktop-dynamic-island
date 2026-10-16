namespace DesktopDynamicIsland.Services;

public static class TaskbarRecoveryPolicy
{
    public const int MaximumAttempts = 15;

    public static bool ShouldRetry(
        CollapsedPlacementMode placementMode,
        bool taskbarWasFound,
        int attemptsCompleted) =>
        placementMode == CollapsedPlacementMode.WorkAreaBottomRightFallback &&
        !taskbarWasFound &&
        attemptsCompleted < MaximumAttempts;
}
