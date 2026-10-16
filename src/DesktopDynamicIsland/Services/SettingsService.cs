using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using DesktopDynamicIsland.Models;

namespace DesktopDynamicIsland.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AppLogger _logger;

    public SettingsService(AppLogger logger)
    {
        _logger = logger;
    }

    public IslandSettings Load()
    {
        AppPaths.EnsureDirectories();

        if (!File.Exists(AppPaths.SettingsFile))
        {
            var defaults = CreateDefault();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFile);
            var settings = JsonSerializer.Deserialize<IslandSettings>(json, JsonOptions) ?? CreateDefault();
            Normalize(settings);
            return settings;
        }
        catch (Exception ex)
        {
            BackupBrokenSettings();
            _logger.Error("配置文件损坏，已备份并重建默认配置。", ex);
            var defaults = CreateDefault();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(IslandSettings settings)
    {
        try
        {
            Normalize(settings);
            AppPaths.EnsureDirectories();
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(AppPaths.SettingsFile, json);
        }
        catch (Exception ex)
        {
            _logger.Error("保存配置失败。", ex);
        }
    }

    private static IslandSettings CreateDefault() => new();

    private static void Normalize(IslandSettings settings)
    {
        if (!settings.ExpandedX.HasValue && !settings.ExpandedY.HasValue &&
            string.Equals(settings.Position, "custom", StringComparison.OrdinalIgnoreCase))
        {
            settings.ExpandedX = settings.X;
            settings.ExpandedY = settings.Y;
        }

        if (settings.SamplingIntervalSeconds < 3)
        {
            settings.SamplingIntervalSeconds = 5;
        }

        if (settings.Opacity < 0.6 || settings.Opacity > 1)
        {
            settings.Opacity = 0.92;
        }

        if (string.IsNullOrWhiteSpace(settings.Position))
        {
            settings.Position = "bottom-right";
        }

    }

    private void BackupBrokenSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return;
            }

            var backup = Path.Combine(
                AppPaths.ConfigDirectory,
                $"settings.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(AppPaths.SettingsFile, backup);
        }
        catch (Exception ex)
        {
            _logger.Error("备份损坏配置失败。", ex);
        }
    }
}
