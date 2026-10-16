using DesktopDynamicIsland.Models;

namespace DesktopDynamicIsland.Services;

public readonly record struct PerformanceAlert(
    MetricPair TargetPair,
    string Title,
    string Content,
    string Level);

public sealed class PerformanceAlertEngine
{
    private const double Gibibyte = 1024d * 1024 * 1024;
    private readonly SustainedAlertGate _cpuTemperature = CreateGate(60, 15, 120, 30);
    private readonly SustainedAlertGate _gpuTemperature = CreateGate(120, 30, 120, 30);
    private readonly SustainedAlertGate _ram = CreateGate(60, 15, 120, 20);
    private readonly SustainedAlertGate _vram = CreateGate(45, 15, 120, 20);
    private SystemStatus _latest = new();

    public void Update(SystemStatus status, DateTimeOffset now)
    {
        _latest = status;

        var cpuTemperature = status.CpuTemperatureCelsius;
        _cpuTemperature.Update(
            cpuTemperature >= 95,
            cpuTemperature >= 99,
            cpuTemperature <= 88,
            now);

        var gpuTemperature = status.GpuTemperatureCelsius;
        _gpuTemperature.Update(
            gpuTemperature >= 85,
            gpuTemperature >= 87,
            gpuTemperature <= 78,
            now);

        var available = status.AvailableMemoryBytes;
        var ramExceeded = available is double bytes
            ? status.RamPercent >= 90 && bytes <= 1.5 * Gibibyte
            : status.RamPercent >= 93;
        var ramUrgent = available is double urgentBytes
            ? urgentBytes <= 0.75 * Gibibyte
            : status.RamPercent >= 97;
        var ramRecovered = available is double recoveredBytes
            ? status.RamPercent <= 82 && recoveredBytes >= 2.5 * Gibibyte
            : status.RamPercent <= 82;
        _ram.Update(ramExceeded, ramUrgent, ramRecovered, now);

        var vram = status.VramPercent;
        _vram.Update(vram >= 95, vram >= 98, vram <= 88, now);
    }

    public PerformanceAlert? TryTakeNotification()
    {
        if (_cpuTemperature.TryTakeNotification())
        {
            return new PerformanceAlert(
                MetricPair.CpuRam,
                "CPU 温度偏高",
                $"CPU {_latest.CpuTemperatureCelsius:0}°C，已持续接近温度上限",
                "warning");
        }

        if (_gpuTemperature.TryTakeNotification())
        {
            return new PerformanceAlert(
                MetricPair.GpuVram,
                "GPU 温度偏高",
                $"GPU {_latest.GpuTemperatureCelsius:0}°C，建议检查散热状态",
                "warning");
        }

        if (_ram.TryTakeNotification())
        {
            return new PerformanceAlert(
                MetricPair.CpuRam,
                "内存压力偏高",
                $"RAM {_latest.RamPercent:0}%，可用空间已接近下限",
                "warning");
        }

        if (_vram.TryTakeNotification())
        {
            return new PerformanceAlert(
                MetricPair.GpuVram,
                "显存压力偏高",
                $"VRAM {_latest.VramPercent:0}%，已接近容量上限",
                "warning");
        }

        return null;
    }

    private static SustainedAlertGate CreateGate(
        int sustainedSeconds,
        int urgentSeconds,
        int recoverySeconds,
        int cooldownMinutes) => new(
            TimeSpan.FromSeconds(sustainedSeconds),
            TimeSpan.FromSeconds(urgentSeconds),
            TimeSpan.FromSeconds(recoverySeconds),
            TimeSpan.FromMinutes(cooldownMinutes));
}
