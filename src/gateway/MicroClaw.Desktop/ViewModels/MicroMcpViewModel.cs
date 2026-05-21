namespace MicroClaw.Desktop;

public sealed class MicroMcpViewModel : ViewModelBase
{
    public string Title { get; } = "全局 MCP";

    public string Description { get; } = "汇总 MCP 服务的连接状态、暴露工具与接入范围。";

    public string SectionTitle { get; } = "服务状态";

    public IReadOnlyList<StaticMetric> Metrics { get; } =
        [
                new StaticMetric("已注册服务", "3", "本地、文件与浏览器桥接占位"),
                new StaticMetric("在线服务", "2", "静态状态展示"),
                new StaticMetric("待审核", "1", "新接入服务需人工确认")
        ];

    public IReadOnlyList<StaticEntry> Items { get; } =
        [
                new StaticEntry("filesystem", "提供工作区浏览、读取与写入能力。", "在线", "作用域：当前仓库"),
                new StaticEntry("browser", "保留给浏览器项目二期的工具入口。", "待接线", "当前为静态配置"),
                new StaticEntry("scheduler", "承接周期任务与定时触发。", "在线", "下一步接心跳与任务管理")
        ];
}
