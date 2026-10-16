using System.Globalization;

namespace DesktopDynamicIsland.Models;

public enum MetricSeverity
{
    Normal,
    Warning,
    Danger
}

public sealed class SystemStatus
{
    public double CpuPercent { get; set; }
    public double RamPercent { get; set; }
    public double? AvailableMemoryBytes { get; set; }
    public double? GpuPercent { get; set; }
    public double? GpuMemoryUsedBytes { get; set; }
    public double? GpuMemoryTotalBytes { get; set; }
    public double? GpuTemperatureCelsius { get; set; }
    public double? DiskWriteBytesPerSecond { get; set; }
    public double? CpuTemperatureCelsius { get; set; }
    public string TemperatureSource { get; set; } = "TEMP";

    public double MemoryPercent
    {
        get => RamPercent;
        set => RamPercent = value;
    }

    public string PowerText { get; set; } = "电源";
    public double DiskPercent { get; set; }
    public string NetworkText { get; set; } = "待接入";
    public DateTime SampleTime { get; set; } = DateTime.Now;

    public string CpuText => $"CPU {ClampPercent(CpuPercent):0}%";
    public string RamText => $"RAM {ClampPercent(RamPercent):0}%";
    public string GpuText => GpuPercent is double gpu && gpu >= 0
        ? $"GPU {ClampPercent(gpu):0}%"
        : "GPU —";
    public string GpuTemperatureText => GpuTemperatureCelsius is double temperature && temperature is > 0 and < 125
        ? $"GPU温度 {temperature:0}°C"
        : "GPU温度 —";
    public string GpuTemperatureValueText => GpuTemperatureCelsius is double temperature && temperature is > 0 and < 125
        ? $"{temperature:0}°C"
        : "—";
    public string TemperatureText => GpuTemperatureText;
    public MetricSeverity CpuSeverity => ClassifyPercent(CpuPercent);
    public MetricSeverity RamSeverity => ClassifyPercent(RamPercent);
    public MetricSeverity GpuSeverity => GpuPercent is double gpu
        ? ClassifyPercent(gpu)
        : MetricSeverity.Normal;
    public MetricSeverity VramSeverity => VramPercent is double vram
        ? ClassifyPercent(vram)
        : MetricSeverity.Normal;
    public MetricSeverity GpuTemperatureSeverity => GpuTemperatureCelsius switch
    {
        >= 85 => MetricSeverity.Danger,
        >= 78 => MetricSeverity.Warning,
        _ => MetricSeverity.Normal
    };
    public string LoadStatusText
    {
        get
        {
            if (GpuTemperatureCelsius >= 85)
            {
                return "GPU温度过高";
            }

            if (RamPercent >= 90 || AvailableMemoryBytes is <= 1.5 * 1024 * 1024 * 1024)
            {
                return "内存压力过高";
            }

            if (VramPercent >= 95)
            {
                return "显存压力过高";
            }

            if (CpuPercent >= 90)
            {
                return "CPU负载过高";
            }

            if (GpuPercent >= 95)
            {
                return "GPU负载过高";
            }

            return CpuPercent >= 80 ||
                   RamPercent >= 80 ||
                   GpuPercent >= 85 ||
                   VramPercent >= 90 ||
                   GpuTemperatureCelsius >= 78
                ? "负载偏高"
                : "负载正常";
        }
    }
    public MetricSeverity LoadStatusSeverity => LoadStatusText switch
    {
        "负载正常" => MetricSeverity.Normal,
        "负载偏高" => MetricSeverity.Warning,
        _ => MetricSeverity.Danger
    };
    public double? VramPercent =>
        GpuMemoryUsedBytes is double used &&
        GpuMemoryTotalBytes is double total &&
        used >= 0 &&
        total > 0
            ? ClampPercent(used * 100 / total)
            : null;

    public string VramText
    {
        get
        {
            if (GpuMemoryUsedBytes is not double used || used < 0)
            {
                return "VRAM —";
            }

            if (GpuMemoryTotalBytes is double total && total > 0)
            {
                var percent = ClampPercent(used * 100 / total);
                return $"VRAM {percent:0}%";
            }

            return $"VRAM {FormatCapacity(used)}";
        }
    }

    public string DiskWriteText => DiskWriteBytesPerSecond is double bytes && bytes >= 0
        ? $"写入 {FormatRate(bytes)}"
        : "写入 —";

    public string Summary
    {
        get
        {
            if (CpuPercent >= 80)
            {
                return "负载较高";
            }

            if (RamPercent >= 80)
            {
                return "内存偏高";
            }

            return "正常";
        }
    }

    private static double ClampPercent(double value) => Math.Clamp(value, 0, 100);

    private static MetricSeverity ClassifyPercent(double value)
    {
        var percent = ClampPercent(value);
        return percent switch
        {
            >= 90 => MetricSeverity.Danger,
            >= 70 => MetricSeverity.Warning,
            _ => MetricSeverity.Normal
        };
    }

    private static string FormatRate(double bytes)
    {
        const double kilo = 1024;
        const double mega = kilo * 1024;
        const double giga = mega * 1024;

        if (bytes >= giga)
        {
            return $"{FormatNumber(bytes / giga)} GB/s";
        }

        if (bytes >= mega)
        {
            return $"{FormatNumber(bytes / mega)} MB/s";
        }

        return $"{FormatNumber(bytes / kilo)} KB/s";
    }

    private static string FormatCapacity(double bytes)
    {
        const double mega = 1024 * 1024;
        const double giga = mega * 1024;

        return bytes >= giga
            ? $"{FormatNumber(bytes / giga)} GB"
            : $"{FormatNumber(bytes / mega)} MB";
    }

    private static string FormatNumber(double value) =>
        value.ToString(value >= 10 ? "0" : "0.#", CultureInfo.InvariantCulture);
}
