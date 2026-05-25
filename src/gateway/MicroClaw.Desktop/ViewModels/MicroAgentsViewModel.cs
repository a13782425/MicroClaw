
namespace MicroClaw.Desktop.ViewModels;

[PageRoute(PageRouteDefine.RouteMicroAgents)]
public sealed class MicroAgentsViewModel : RouteViewModelBase
{
    public string Title { get; } = "全局 Agent";

    public string Description { get; } = "集中配置默认代理、角色边界与分发策略。";

    public string SectionTitle { get; } = "当前预览";

    public IReadOnlyList<StaticMetric> Metrics { get; } =
        [
                new StaticMetric("默认代理", "1", "会话会优先落到全局默认代理"),
                new StaticMetric("已挂载能力", "12", "来自技能、工具与渠道的静态占位"),
                new StaticMetric("运行策略", "自动", "按优先级选择模型与工具")
        ];

    public IReadOnlyList<StaticEntry> Items { get; } =
        [
                new StaticEntry("协调代理", "负责路由分派、工具调用与上下文汇总。", "启用中", "Claude Sonnet 4.5"),
                new StaticEntry("执行代理", "处理需要长链路推理与计划拆解的任务。", "待扩展", "最大迭代 8 次"),
                new StaticEntry("渠道代理", "为桌面、企微与飞书入口保留独立行为参数。", "预留", "下一阶段接真实配置")
        ];
}
