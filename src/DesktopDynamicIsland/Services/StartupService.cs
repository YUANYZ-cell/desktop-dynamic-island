using System.IO;
using System.Diagnostics;
using Microsoft.Win32;

namespace DesktopDynamicIsland.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopDynamicIsland";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, AppLogger logger, out string error)
    {
        error = "";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                var executable = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                {
                    error = "找不到当前程序路径，无法写入开机启动。";
                    logger.Warn(error);
                    return false;
                }

                key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
                logger.Info("已开启当前用户级开机自启动。");
                return true;
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
            logger.Info("已关闭当前用户级开机自启动。");
            return true;
        }
        catch (Exception ex)
        {
            error = $"开机自启动设置失败：{ex.Message}";
            logger.Error("开机自启动设置失败。", ex);
            return false;
        }
    }
}
