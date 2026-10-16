using DesktopDynamicIsland.Services;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class TaskbarRecoveryPolicyTests
{
    [Fact]
    public void ShouldRetry_WhenTaskbarIsTemporarilyMissingAfterDisplayChange()
    {
        Assert.True(TaskbarRecoveryPolicy.ShouldRetry(
            CollapsedPlacementMode.WorkAreaBottomRightFallback,
            taskbarWasFound: false,
            attemptsCompleted: 0));
    }

    [Fact]
    public void ShouldRetry_StopsAfterTaskbarOverlayIsRecovered()
    {
        Assert.False(TaskbarRecoveryPolicy.ShouldRetry(
            CollapsedPlacementMode.TaskbarOverlay,
            taskbarWasFound: true,
            attemptsCompleted: 1));
    }

    [Fact]
    public void ShouldRetry_StopsAfterMaximumAttempts()
    {
        Assert.False(TaskbarRecoveryPolicy.ShouldRetry(
            CollapsedPlacementMode.WorkAreaBottomRightFallback,
            taskbarWasFound: false,
            attemptsCompleted: TaskbarRecoveryPolicy.MaximumAttempts));
    }
}
