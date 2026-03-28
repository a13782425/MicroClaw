namespace MicroClaw.Tools;

/// <summary>
/// 统一工具提供者接口 — 替代 IBuiltinToolProvider 和 IChannelToolProvider，
/// 所有工具来源（内置、渠道、MCP、技能）统一实现此接口并注册到 DI。
/// ToolCollector 自动发现并按 Agent 配置过滤。
/// </summary>
public interface IToolProvider
{
    /// <summary>工具分类（内置 / 渠道 / MCP）。</summary>
    ToolCategory Category { get; }

    /// <summary>工具分组标识，对应 ToolGroupConfig.GroupId，用于按 Agent 配置启用/禁用。</summary>
    string GroupId { get; }

    /// <summary>分组的展示名称（用于 UI 展示），消除硬编码 displayName 映射。</summary>
    string DisplayName { get; }

    /// <summary>返回工具的元数据描述列表（不需要运行时上下文，用于 UI 展示）。</summary>
    IReadOnlyList<(string Name, string Description)> GetToolDescriptions();

    /// <summary>
    /// 按运行时上下文创建工具实例列表。
    /// 不适用的 Provider（如渠道类型不匹配、缺少 sessionId）返回 <see cref="ToolProviderResult.Empty"/>。
    /// </summary>
    Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default);
}
