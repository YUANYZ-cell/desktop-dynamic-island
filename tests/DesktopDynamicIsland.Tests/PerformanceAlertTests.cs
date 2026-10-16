using DesktopDynamicIsland.Models;
using DesktopDynamicIsland.Services;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class PerformanceAlertTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SustainedAlertGate_NotifiesOnceAfterSustainedDuration()
    {
        var gate = new SustainedAlertGate(
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(120),
            TimeSpan.FromMinutes(20));

        gate.Update(exceeded: true, urgent: false, recovered: false, Start);
        gate.Update(exceeded: true, urgent: false, recovered: false, Start.AddSeconds(59));
        Assert.False(gate.TryTakeNotification());

        gate.Update(exceeded: true, urgent: false, recovered: false, Start.AddSeconds(60));
        Assert.True(gate.TryTakeNotification());
        Assert.False(gate.TryTakeNotification());
    }

    [Fact]
    public void SustainedAlertGate_UrgentConditionUsesShorterDuration()
    {
        var gate = new SustainedAlertGate(
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(120),
            TimeSpan.FromMinutes(20));

        gate.Update(exceeded: true, urgent: true, recovered: false, Start);
        gate.Update(exceeded: true, urgent: true, recovered: false, Start.AddSeconds(15));

        Assert.True(gate.TryTakeNotification());
    }

    [Fact]
    public void SustainedAlertGate_RequiresRecoveryAndCooldownBeforeRearming()
    {
        var gate = new SustainedAlertGate(
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(120),
            TimeSpan.FromMinutes(20));

        gate.Update(true, false, false, Start);
        gate.Update(true, false, false, Start.AddSeconds(60));
        Assert.True(gate.TryTakeNotification());

        gate.Update(false, false, true, Start.AddSeconds(61));
        gate.Update(false, false, true, Start.AddSeconds(181));
        gate.Update(true, true, false, Start.AddMinutes(10));
        gate.Update(true, true, false, Start.AddMinutes(10).AddSeconds(30));
        Assert.False(gate.TryTakeNotification());

        var rearm = Start.AddSeconds(181).AddMinutes(20);
        gate.Update(true, true, false, rearm);
        gate.Update(true, true, false, rearm.AddSeconds(15));
        Assert.True(gate.TryTakeNotification());
    }

    [Fact]
    public void MetricPairController_AlertOverrideRestoresSelectedPair()
    {
        var controller = new MetricPairController(MetricPair.CpuRam);

        controller.Override(MetricPair.GpuVram, Start, TimeSpan.FromSeconds(18));

        Assert.Equal(MetricPair.GpuVram, controller.GetEffectivePair(Start.AddSeconds(17)));
        Assert.Equal(MetricPair.CpuRam, controller.GetEffectivePair(Start.AddSeconds(18)));
    }

    [Fact]
    public void PerformanceAlertEngine_GpuTemperatureProducesGpuTargetOnce()
    {
        var engine = new PerformanceAlertEngine();
        var status = new SystemStatus { GpuTemperatureCelsius = 87 };

        engine.Update(status, Start);
        engine.Update(status, Start.AddSeconds(30));

        var alert = engine.TryTakeNotification();
        Assert.NotNull(alert);
        Assert.Equal(MetricPair.GpuVram, alert.Value.TargetPair);
        Assert.Contains("87°C", alert.Value.Content);
        Assert.Null(engine.TryTakeNotification());
    }
}
