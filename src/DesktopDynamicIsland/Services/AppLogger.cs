using System.IO;

namespace DesktopDynamicIsland.Services;

public sealed class AppLogger
{
    private const long MaxLogBytes = 512 * 1024;
    private readonly object _lock = new();
    private readonly string _path;

    public AppLogger(string path)
    {
        _path = path;
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", detail);
    }

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            RotateIfNeeded();
            File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
        }
    }

    private void RotateIfNeeded()
    {
        var file = new FileInfo(_path);
        if (!file.Exists || file.Length < MaxLogBytes)
        {
            return;
        }

        var backup = Path.Combine(file.DirectoryName!, "app.previous.log");
        if (File.Exists(backup))
        {
            File.Delete(backup);
        }

        File.Move(_path, backup);
    }
}
