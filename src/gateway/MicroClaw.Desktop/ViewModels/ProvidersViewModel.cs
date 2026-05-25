namespace MicroClaw.Desktop.ViewModels;

[PageRoute(PageRouteDefine.RouteSettingsProviders)]
public sealed class ProvidersViewModel : RouteViewModelBase
{
    public string Title { get; } = "模型提供商";

    public string Description { get; } = "从 Footer 设置菜单进入，集中查看模型提供商与默认模型策略。";

    public string SectionTitle { get; } = "提供商状态";

    public IReadOnlyList<StaticMetric> Metrics { get; } =
        [
                new StaticMetric("已配置提供商", "3", "OpenAI、Claude、本地模型"),
                new StaticMetric("默认提供商", "Claude", "桌面预览默认指向 Anthropic"),
                new StaticMetric("密钥来源", "环境变量", "不会在桌面静态页中明文展示")
        ];

    public IReadOnlyList<StaticEntry> Items { get; } =
        [
                new StaticEntry("Anthropic", "用于默认对话与协调代理。", "已启用", "Claude Sonnet 4.5"),
                new StaticEntry("OpenAI", "预留给工具密集型任务与兼容性回退。", "可切换", "GPT-4.1 / o4-mini"),
                new StaticEntry("Local", "本地推理与离线实验入口。", "待配置", "需要显式模型路径")
        ];
}
