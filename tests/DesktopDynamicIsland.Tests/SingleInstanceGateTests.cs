using DesktopDynamicIsland.Services;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class SingleInstanceGateTests
{
    [Fact]
    public void OnlyFirstGateWithSameNameAcquiresOwnership()
    {
        var mutexName = $"DesktopDynamicIsland.Tests.{Guid.NewGuid():N}";

        using var first = SingleInstanceGate.TryAcquire(mutexName);
        using var second = SingleInstanceGate.TryAcquire(mutexName);

        Assert.NotNull(first);
        Assert.Null(second);
    }
}
