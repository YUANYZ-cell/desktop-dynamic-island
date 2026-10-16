using System.Runtime.InteropServices;

namespace DesktopDynamicIsland.Services;

public readonly record struct NvidiaMetricsSample(
    double? GpuPercent,
    double? TemperatureCelsius,
    double? UsedMemoryBytes,
    double? TotalMemoryBytes);

public sealed class NvidiaMetricsReader : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);
    private IntPtr _device;
    private bool _initialized;
    private bool _disposed;
    private DateTimeOffset _lastRead = DateTimeOffset.MinValue;
    private NvidiaMetricsSample _cached;

    public NvidiaMetricsReader()
    {
        try
        {
            _initialized = NvmlInit() == 0 && NvmlDeviceGetHandleByIndex(0, out _device) == 0;
            if (!_initialized)
            {
                _device = IntPtr.Zero;
            }
        }
        catch (DllNotFoundException)
        {
            _initialized = false;
        }
        catch (EntryPointNotFoundException)
        {
            _initialized = false;
        }
    }

    public NvidiaMetricsSample Read(DateTimeOffset now)
    {
        if (_disposed || !_initialized || _device == IntPtr.Zero)
        {
            return default;
        }

        if (now - _lastRead < RefreshInterval)
        {
            return _cached;
        }

        _lastRead = now;
        double? utilization = null;
        double? temperature = null;
        double? usedMemory = null;
        double? totalMemory = null;

        try
        {
            if (NvmlDeviceGetUtilizationRates(_device, out var rates) == 0)
            {
                utilization = Math.Clamp(rates.Gpu, 0u, 100u);
            }

            if (NvmlDeviceGetTemperature(_device, 0, out var value) == 0 && value is > 0 and < 125)
            {
                temperature = value;
            }

            if (NvmlDeviceGetMemoryInfo(_device, out var memory) == 0 && memory.Total > 0)
            {
                totalMemory = memory.Total;
                usedMemory = Math.Min(memory.Used, memory.Total);
            }
        }
        catch (SEHException)
        {
            // The display driver can restart while a cached NVML handle is in use.
        }

        _cached = new NvidiaMetricsSample(utilization, temperature, usedMemory, totalMemory);
        return _cached;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_initialized)
        {
            return;
        }

        try
        {
            _ = NvmlShutdown();
        }
        catch (SEHException)
        {
        }

        _initialized = false;
        _device = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    private static extern int NvmlInit();

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
    private static extern int NvmlShutdown();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int NvmlDeviceGetTemperature(IntPtr device, uint sensorType, out uint temperature);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    private static extern int NvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
    private static extern int NvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);
}
