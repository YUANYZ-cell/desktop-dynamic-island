namespace DesktopDynamicIsland.Models;

public sealed class IslandMessage
{
    public string Type { get; set; } = "normal";
    public string Title { get; set; } = "消息";
    public string Content { get; set; } = "";
    public string Level { get; set; } = "info";
    public DateTime Time { get; set; }

    public string ShortTime => Time == default ? "--:--" : Time.ToString("HH:mm");
}
