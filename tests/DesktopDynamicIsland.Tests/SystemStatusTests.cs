using DesktopDynamicIsland.Models;
using DesktopDynamicIsland.Services;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class SystemStatusTests
{
    [Fact]
    public void GpuText_FormatsAvailableUtilization()
    {
        Assert.Equal("GPU 51%", new SystemStatus { GpuPercent = 51.2 }.GpuText);
    }

    [Fact]
    public void GpuText_ReportsUnavailableUtilization()
    {
        Assert.Equal("GPU —", new SystemStatus().GpuText);
    }

    [Fact]
    public void GpuTemperatureText_FormatsTrustedNvmlTemperature()
    {
        var status = new SystemStatus { GpuTemperatureCelsius = 57.4 };

        Assert.Equal("GPU温度 57°C", status.GpuTemperatureText);
    }

    [Fact]
    public void GpuTemperatureText_ReportsUnavailableTemperature()
    {
        Assert.Equal("GPU温度 —", new SystemStatus().GpuTemperatureText);
    }

    [Theory]
    [InlineData(62.4, "62°C")]
    [InlineData(null, "—")]
    public void GpuTemperatureValueText_FitsCompactMetricGrid(double? temperature, string expected)
    {
        var status = new SystemStatus { GpuTemperatureCelsius = temperature };

        Assert.Equal(expected, status.GpuTemperatureValueText);
    }

    [Theory]
    [InlineData(12, 55, 6, 20, 52, "负载正常", MetricSeverity.Normal)]
    [InlineData(82, 55, 6, 20, 52, "负载偏高", MetricSeverity.Warning)]
    [InlineData(92, 55, 6, 20, 52, "CPU负载过高", MetricSeverity.Danger)]
    [InlineData(12, 92, 6, 20, 52, "内存压力过高", MetricSeverity.Danger)]
    [InlineData(12, 55, 96, 20, 52, "GPU负载过高", MetricSeverity.Danger)]
    [InlineData(12, 55, 6, 96, 52, "显存压力过高", MetricSeverity.Danger)]
    [InlineData(12, 55, 6, 20, 85, "GPU温度过高", MetricSeverity.Danger)]
    public void LoadStatus_DescribesCurrentPressureWithoutHistory(
        double cpu,
        double ram,
        double gpu,
        double vram,
        double gpuTemperature,
        string expectedText,
        MetricSeverity expectedSeverity)
    {
        var status = new SystemStatus
        {
            CpuPercent = cpu,
            RamPercent = ram,
            GpuPercent = gpu,
            GpuMemoryUsedBytes = vram,
            GpuMemoryTotalBytes = 100,
            GpuTemperatureCelsius = gpuTemperature
        };

        Assert.Equal(expectedText, status.LoadStatusText);
        Assert.Equal(expectedSeverity, status.LoadStatusSeverity);
    }

    [Theory]
    [InlineData(77, MetricSeverity.Normal)]
    [InlineData(78, MetricSeverity.Warning)]
    [InlineData(84, MetricSeverity.Warning)]
    [InlineData(85, MetricSeverity.Danger)]
    public void GpuTemperatureSeverity_UsesPersonalizedThresholds(double value, MetricSeverity expected)
    {
        Assert.Equal(expected, new SystemStatus { GpuTemperatureCelsius = value }.GpuTemperatureSeverity);
    }

    [Theory]
    [InlineData(0, "写入 0 KB/s")]
    [InlineData(1536, "写入 1.5 KB/s")]
    [InlineData(12582912, "写入 12 MB/s")]
    [InlineData(1610612736, "写入 1.5 GB/s")]
    public void DiskWriteText_UsesReadableUnits(double value, string expected)
    {
        var status = new SystemStatus { DiskWriteBytesPerSecond = value };

        Assert.Equal(expected, status.DiskWriteText);
    }

    [Fact]
    public void DiskWriteText_ShowsUnavailableWhenSampleIsMissing()
    {
        Assert.Equal("写入 —", new SystemStatus().DiskWriteText);
    }

    [Fact]
    public void VramText_UsesPercentageWhenTotalIsKnown()
    {
        var status = new SystemStatus
        {
            GpuMemoryUsedBytes = 4 * 1024d * 1024 * 1024,
            GpuMemoryTotalBytes = 8 * 1024d * 1024 * 1024
        };

        Assert.Equal("VRAM 50%", status.VramText);
    }

    [Fact]
    public void VramText_FallsBackToUsedCapacityWhenTotalIsUnknown()
    {
        var status = new SystemStatus
        {
            GpuMemoryUsedBytes = 2.5 * 1024 * 1024 * 1024
        };

        Assert.Equal("VRAM 2.5 GB", status.VramText);
    }

    [Fact]
    public void VramText_ShowsUnavailableWhenSampleIsMissing()
    {
        Assert.Equal("VRAM —", new SystemStatus().VramText);
    }

    [Theory]
    [InlineData(-12, "RAM 0%")]
    [InlineData(61.4, "RAM 61%")]
    [InlineData(120, "RAM 100%")]
    public void RamText_ClampsPercentage(double value, string expected)
    {
        Assert.Equal(expected, new SystemStatus { RamPercent = value }.RamText);
    }

    [Theory]
    [InlineData(0, MetricSeverity.Normal)]
    [InlineData(69, MetricSeverity.Normal)]
    [InlineData(70, MetricSeverity.Warning)]
    [InlineData(89, MetricSeverity.Warning)]
    [InlineData(90, MetricSeverity.Danger)]
    [InlineData(100, MetricSeverity.Danger)]
    public void CpuSeverity_UsesVisibleLoadThresholds(double value, MetricSeverity expected)
    {
        Assert.Equal(expected, new SystemStatus { CpuPercent = value }.CpuSeverity);
    }

    [Theory]
    [InlineData(0, MetricSeverity.Normal)]
    [InlineData(69, MetricSeverity.Normal)]
    [InlineData(70, MetricSeverity.Warning)]
    [InlineData(89, MetricSeverity.Warning)]
    [InlineData(90, MetricSeverity.Danger)]
    [InlineData(100, MetricSeverity.Danger)]
    public void RamSeverity_UsesVisibleLoadThresholds(double value, MetricSeverity expected)
    {
        Assert.Equal(expected, new SystemStatus { RamPercent = value }.RamSeverity);
    }
}
