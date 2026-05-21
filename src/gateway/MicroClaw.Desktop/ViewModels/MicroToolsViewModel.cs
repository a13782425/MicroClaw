namespace MicroClaw.Desktop;

public sealed class MicroToolsViewModel : ViewModelBase
{
    public string Title { get; } = "全局 Tools";

    public string Description { get; } = "声明桌面会话默认可用的工具集合与权限边界。";

    public string SectionTitle { get; } = "工具概览";

    public IReadOnlyList<StaticMetric> Metrics { get; } =
        [
                new StaticMetric("总工具数", "18", "内置工具与 MCP 工具合并后展示"),
                new StaticMetric("高权限", "4", "需要额外确认的工具"),
                new StaticMetric("默认开放", "9", "所有会话启动即注入")
        ];

    public IReadOnlyList<StaticEntry> Items { get; } =
        [
                new StaticEntry("Fetch", "抓取网页内容并回传结构化文本。", "可用", "超时 30s"),
                new StaticEntry("Shell", "执行受控终端命令，后续配合风险策略。", "受限", "高权限"),
                new StaticEntry("File", "读写工作区文件并支持轻量编辑。", "可用", "桌面端重点能力")
        ];
}
