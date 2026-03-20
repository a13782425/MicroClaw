namespace MicroClaw.Tools;

/// <summary>
/// 用于 Agent 级别的工具分组启用配置。
/// 每个分组对应一个内置工具组（如 "cron"）或一个 MCP Server（Name 即 GroupId）。
/// </summary>
public sealed record ToolGroupConfig(
    /// <summary>分组标识。内置分组使用固定名称如 "cron"，MCP 分组使用 McpServerConfig.Name。</summary>
    string GroupId,
    /// <summary>是否整体启用该分组。为 false 时跳过组内全部工具。</summary>
    bool IsEnabled,
    /// <summary>组内单独禁用的工具名列表。分组整体启用时仍可禁用个别工具。</summary>
    IReadOnlyList<string> DisabledToolNames);
