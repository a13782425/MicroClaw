using MicroClaw.Configuration;
using YamlDotNet.Serialization;

namespace MicroClaw.Desktop.Config;

[MicroClawYamlConfig("microclaw", FileName = "microclaw.yaml")]
public sealed class MicroClawOptions : IMicroClawConfigTemplate
{
    [YamlMember(Alias = "desktop", Description = "桌面配置")]
    public DesktopOptions Desktop { get; set; } = new DesktopOptions();
    public IMicroClawConfigOptions CreateDefaultTemplate()
    {
        return new MicroClawOptions();
    }
}
public sealed class DesktopOptions
{
    // === 主题 ===
    [YamlMember(Alias = "theme_mode")]
    public string ThemeMode { get; set; } = "System"; // System | Light | Dark

    // === 窗口 ===
    [YamlMember(Alias = "window_width")]
    public double WindowWidth { get; set; } = 1366;

    [YamlMember(Alias = "window_height")]
    public double WindowHeight { get; set; } = 768;


    // === 侧边栏 ===
    [YamlMember(Alias = "sidebar_expanded")]
    public bool SidebarExpanded { get; set; } = true;

}