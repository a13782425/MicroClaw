namespace MicroClaw.Desktop.ViewModels;

[PageRoute(PageRouteDefine.RouteMicroPlugins)]
public sealed class MicroPluginsViewModel : RouteViewModelBase
{
    public string Title { get; } = "全局插件";

    public string Description { get; } = "展示插件生命周期、加载状态与升级入口。";

    public string SectionTitle { get; } = "插件状态";

    public IReadOnlyList<StaticMetric> Metrics { get; } =
        [
                new StaticMetric("已发现插件", "5", "扫描本地插件目录的静态样本"),
                new StaticMetric("已加载", "4", "随应用启动自动激活"),
                new StaticMetric("待升级", "1", "有版本差异但未执行更新")
        ];

    public IReadOnlyList<StaticEntry> Items { get; } =
        [
                new StaticEntry("workflow-kit", "为工作流相关页面提供节点与模板。", "已加载", "版本 0.9.2"),
                new StaticEntry("browser-bridge", "桥接浏览器项目二期能力。", "预留", "尚未接入真实进程"),
                new StaticEntry("desktop-labs", "承载桌面壳的实验性 UI 能力。", "开发中", "仅静态展示")
        ];
}
