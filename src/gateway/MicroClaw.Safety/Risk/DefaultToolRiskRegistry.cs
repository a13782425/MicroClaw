namespace MicroClaw.Safety;

/// <summary>
/// 默认工具风险等级注册表。
/// 为所有内置工具预设合理的风险等级，支持通过构造参数追加或覆盖自定义标注。
/// </summary>
public sealed class DefaultToolRiskRegistry : IToolRiskRegistry
{
    /// <summary>内置工具的默认风险标注。</summary>
    private static readonly IReadOnlyList<ToolRiskAnnotation> BuiltinAnnotations =
    [
        // Shell 工具
        new("exec_command",    RiskLevel.Critical, "任意 Shell 命令执行，可能对系统造成不可逆影响"),

        // 文件工具
        new("read_file",       RiskLevel.Low,      "只读操作，不修改文件系统"),
        new("list_directory",  RiskLevel.Low,      "只读操作，不修改文件系统"),
        new("search_files",    RiskLevel.Low,      "只读操作，不修改文件系统"),
        new("write_file",      RiskLevel.High,     "创建或覆盖文件，对文件系统有明显影响"),
        new("edit_file",       RiskLevel.High,     "修改已有文件内容"),

        // 网络工具
        new("fetch_url",       RiskLevel.Medium,   "向外部 URL 发起 HTTP 请求，可能泄露数据或触发副作用"),

        // 定时任务工具
        new("list_cron_jobs",  RiskLevel.Low,      "只读操作，仅查询定时任务列表"),
        new("get_current_time",RiskLevel.Low,      "只读操作，获取服务器时间"),
        new("create_cron_job", RiskLevel.Medium,   "创建定时任务，有潜在的自动化操作风险"),
        new("update_cron_job", RiskLevel.Medium,   "修改已有定时任务配置"),
        new("delete_cron_job", RiskLevel.Medium,   "删除定时任务"),
    ];

    private readonly IReadOnlyDictionary<string, ToolRiskAnnotation> _annotationMap;
    private readonly IReadOnlyList<ToolRiskAnnotation> _allAnnotations;

    /// <summary>
    /// 创建默认注册表实例（仅使用内置标注）。
    /// </summary>
    public DefaultToolRiskRegistry() : this((IReadOnlyList<ToolRiskAnnotation>?)null) { }

    /// <summary>
    /// 创建默认注册表实例。
    /// </summary>
    /// <param name="customAnnotations">
    /// 自定义标注（可选）。与内置标注同名时覆盖内置值，未知工具名时追加为新条目。
    /// </param>
    public DefaultToolRiskRegistry(IReadOnlyList<ToolRiskAnnotation>? customAnnotations = null)
    {
        // 内置优先，自定义覆盖
        var merged = new Dictionary<string, ToolRiskAnnotation>(StringComparer.OrdinalIgnoreCase);
        foreach (ToolRiskAnnotation a in BuiltinAnnotations)
            merged[a.ToolName] = a;

        if (customAnnotations is not null)
            foreach (ToolRiskAnnotation a in customAnnotations)
                merged[a.ToolName] = a;

        _annotationMap = merged;
        _allAnnotations = merged.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public RiskLevel GetRiskLevel(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return RiskLevel.Low;
        return _annotationMap.TryGetValue(toolName, out ToolRiskAnnotation? annotation)
            ? annotation.RiskLevel
            : RiskLevel.Low; // 未知工具默认低风险
    }

    /// <inheritdoc/>
    public IReadOnlyList<ToolRiskAnnotation> GetAllAnnotations() => _allAnnotations;
}
