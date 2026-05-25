namespace MicroClaw.Desktop.ViewModels;

[PageRoute(PageRouteDefine.RouteMicroSkills)]
public sealed class MicroSkillsViewModel : RouteViewModelBase
{
    public string Title { get; } = "全局 Skill";

    public string Description { get; } = "统一管理技能启用状态、优先级与说明文档。";

    public string SectionTitle { get; } = "技能目录";

    public IReadOnlyList<StaticMetric> Metrics { get; } =
        [
                new StaticMetric("内置技能", "8", "启动后自动可见"),
                new StaticMetric("扩展技能", "4", "来自外部目录或插件"),
                new StaticMetric("默认暴露", "6", "所有会话默认可调用")
        ];

    public IReadOnlyList<StaticEntry> Items { get; } =
        [
                new StaticEntry("会话摘要", "生成最近消息的压缩上下文，减少重复输入。", "启用中", "上下文优先级 P1"),
                new StaticEntry("知识检索", "为会话接入静态 RAG 命中与引用展示。", "已挂载", "命中缓存 24h"),
                new StaticEntry("任务拆解", "把复杂请求拆成阶段性执行步骤。", "试运行", "桌面壳先展示占位")
        ];
}
