using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopDynamicIsland.Models;

namespace DesktopDynamicIsland.Services;

public sealed class SystemStatusService : IDisposable
{
    private CpuSnapshot? _lastCpuSnapshot;
    private readonly NvidiaMetricsReader _nvidiaMetricsReader = new();
    private readonly PerformanceCounter? _diskWriteCounter;
    private double? _smoothedDiskWrite;
    private DateTimeOffset _lastDiskRead = DateTimeOffset.MinValue;

    public SystemStatusService()
    {
        try
        {
            _diskWriteCounter = new PerformanceCounter(
                "PhysicalDisk",
                "Disk Write Bytes/sec",
                "_Total",
                readOnly: true);
            _ = _diskWriteCounter.NextValue();
        }
        catch
        {
            _diskWriteCounter?.Dispose();
            _diskWriteCounter = null;
        }
    }

    public SystemStatus ReadStatus()
    {
        var now = DateTimeOffset.Now;
        var gpu = _nvidiaMetricsReader.Read(now);
        var memory = ReadMemory();
        return new SystemStatus
        {
            CpuPercent = ReadCpuPercent(),
            RamPercent = memory.Percent,
            AvailableMemoryBytes = memory.AvailableBytes,
            GpuPercent = gpu.GpuPercent,
            GpuMemoryUsedBytes = gpu.UsedMemoryBytes,
            GpuMemoryTotalBytes = gpu.TotalMemoryBytes,
            GpuTemperatureCelsius = gpu.TemperatureCelsius,
            DiskWriteBytesPerSecond = ReadDiskWriteBytesPerSecond(now),
            CpuTemperatureCelsius = null,
            TemperatureSource = "CPU",
            SampleTime = now.LocalDateTime
        };
    }

    private double ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return 0;
        }

        var snapshot = new CpuSnapshot(
            ToUInt64(idleTime),
            ToUInt64(kernelTime),
            ToUInt64(userTime));

        if (_lastCpuSnapshot is null)
        {
            _lastCpuSnapshot = snapshot;
            return 0;
        }

        var previous = _lastCpuSnapshot.Value;
        _lastCpuSnapshot = snapshot;

        var idle = snapshot.Idle - previous.Idle;
        var kernel = snapshot.Kernel - previous.Kernel;
        var user = snapshot.User - previous.User;
        var total = kernel + user;

        if (total == 0)
        {
            return 0;
        }

        var active = total > idle ? total - idle : 0;
        return Math.Clamp(active * 100.0 / total, 0, 100);
    }

    private static MemorySample ReadMemory()
    {
        var memory = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(memory))
        {
            return default;
        }

        return new MemorySample(memory.MemoryLoad, memory.AvailablePhysical);
    }

    private double? ReadDiskWriteBytesPerSecond(DateTimeOffset now)
    {
        if (_diskWriteCounter is null)
        {
            return null;
        }

        if (now - _lastDiskRead < TimeSpan.FromSeconds(1))
        {
            return _smoothedDiskWrite;
        }

        _lastDiskRead = now;

        try
        {
            var current = _diskWriteCounter.NextValue();
            if (!float.IsFinite(current) || current < 0)
            {
                return null;
            }

            _smoothedDiskWrite = MetricMath.Smooth(_smoothedDiskWrite, current, 0.35);
            return _smoothedDiskWrite;
        }
        catch
        {
            return null;
        }
    }

    private static ulong ToUInt64(FileTime fileTime)
    {
        return ((ulong)fileTime.HighDateTime << 32) | fileTime.LowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    private readonly record struct CpuSnapshot(ulong Idle, ulong Kernel, ulong User);
    private readonly record struct MemorySample(double Percent, double AvailableBytes);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        public readonly uint LowDateTime;
        public readonly uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        }
    }

    public void Dispose()
    {
        _diskWriteCounter?.Dispose();
        _nvidiaMetricsReader.Dispose();
    }
}
