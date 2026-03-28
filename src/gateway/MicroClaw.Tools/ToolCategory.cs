namespace MicroClaw.Tools;

/// <summary>工具提供者的分类。</summary>
public enum ToolCategory
{
    /// <summary>内置工具（fetch、shell、cron、filesystem、subagent、skill）。</summary>
    Builtin,

    /// <summary>渠道专属工具（飞书、企微等）。</summary>
    Channel,

    /// <summary>MCP 外部工具（通过 MCP Server 动态加载）。</summary>
    Mcp
}
