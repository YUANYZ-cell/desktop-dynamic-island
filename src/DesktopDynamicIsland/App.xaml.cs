using System.Windows;
using System.Windows.Threading;
using DesktopDynamicIsland.Services;

namespace DesktopDynamicIsland;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\DesktopDynamicIsland.SingleInstance";
    private SingleInstanceGate? _singleInstanceGate;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceGate = SingleInstanceGate.TryAcquire(SingleInstanceMutexName);
        if (_singleInstanceGate is null)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceGate?.Dispose();
        _singleInstanceGate = null;
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatal("WPF Dispatcher 未处理异常", e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show(
            $"桌面灵动岛启动或运行时出错：{e.Exception.Message}",
            "桌面灵动岛",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Current.Shutdown();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogFatal("AppDomain 未处理异常", exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogFatal("Task 未观察异常", e.Exception);
        e.SetObserved();
    }

    private static void LogFatal(string message, Exception exception)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var logger = new AppLogger(AppPaths.LogFile);
            logger.Error(message, exception);
        }
        catch
        {
            // Last-chance logging must never throw back into WPF startup.
        }
    }
}
