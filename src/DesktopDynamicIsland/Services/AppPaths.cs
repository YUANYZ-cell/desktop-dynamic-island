using System.IO;

namespace DesktopDynamicIsland.Services;

public static class AppPaths
{
    public static string RootDirectory { get; } = DiscoverRootDirectory();
    public static string ConfigDirectory => Path.Combine(RootDirectory, "config");
    public static string DocsDirectory => Path.Combine(RootDirectory, "docs");
    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");
    public static string LogFile => Path.Combine(DocsDirectory, "app.log");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DocsDirectory);
    }

    private static string DiscoverRootDirectory()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DesktopDynamicIsland.sln")) ||
                    (Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                     Directory.Exists(Path.Combine(directory.FullName, "config"))))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
