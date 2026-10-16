using System.Threading;

namespace DesktopDynamicIsland.Services;

public sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceGate(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstanceGate? TryAcquire(string mutexName)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstanceGate(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
