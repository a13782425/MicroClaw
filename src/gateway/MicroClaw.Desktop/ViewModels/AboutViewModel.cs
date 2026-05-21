namespace MicroClaw.Desktop;

public sealed class AboutViewModel : ViewModelBase
{
    public string Title { get; } = "关于桌面预览";

    public string Description { get; } = "当前页面用于承接设置菜单里的关于入口。";

    public string SectionTitle { get; } = "版本信息";

    public IReadOnlyList<StaticMetric> Metrics { get; } =
        [
                new StaticMetric("渠道", "Desktop Preview", "桌面壳静态模板阶段"),
                new StaticMetric("版本", "0.1-alpha", "主要验证信息架构与路由"),
                new StaticMetric("技术栈", ".NET 10 + Avalonia", "图表采用 LiveCharts2")
        ];

    public IReadOnlyList<StaticEntry> Items { get; } =
        [
                new StaticEntry("共享 Shell", "MainView 统一承载会话、全局配置与设置入口。", "已接入", "路由优先"),
                new StaticEntry("静态图表", "使用情况页展示固定样本数据。", "进行中", "下一步补充图表布局"),
                new StaticEntry("平台外壳", "Windows 与 macOS 外壳仍保持平台隔离。", "未变更", "本次范围外")
        ];
}
