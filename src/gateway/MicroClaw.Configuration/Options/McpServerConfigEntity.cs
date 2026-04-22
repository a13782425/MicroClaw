using YamlDotNet.Serialization;

namespace MicroClaw.Configuration.Options;

/// <summary>
/// 单个 MCP Server 的持久化配置实体。
/// </summary>
public sealed record McpServerConfigEntity
{
    /// <summary>
    /// MCP Server 的唯一标识。
    /// </summary>
    [YamlMember(Alias = "id", Description = "MCP Server 的唯一标识。")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// MCP Server 的展示名称。
    /// </summary>
    [YamlMember(Alias = "name", Description = "MCP Server 的展示名称。")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 传输类型，可选值为 stdio、sse 或 http。
    /// </summary>
    [YamlMember(Alias = "transport_type", Description = "传输类型，可选值为 stdio、sse 或 http。")]
    public string TransportType { get; set; } = "stdio";

    /// <summary>
    /// stdio 传输使用的可执行命令，例如 npx 或 python。
    /// </summary>
    [YamlMember(Alias = "command", Description = "stdio 传输使用的可执行命令，例如 npx 或 python。")]
    public string? Command { get; set; }

    /// <summary>
    /// stdio 传输的命令参数，使用 JSON 数组字符串持久化。
    /// </summary>
    [YamlMember(Alias = "args_json", Description = "stdio 传输的命令参数，使用 JSON 数组字符串持久化。")]
    public string? ArgsJson { get; set; }

    /// <summary>
    /// stdio 传输的环境变量，使用 JSON 对象字符串持久化。
    /// </summary>
    [YamlMember(Alias = "env_json", Description = "stdio 传输的环境变量，使用 JSON 对象字符串持久化。")]
    public string? EnvJson { get; set; }

    /// <summary>
    /// SSE 或 HTTP 传输使用的远程端点 URL。
    /// </summary>
    [YamlMember(Alias = "url", Description = "SSE 或 HTTP 传输使用的远程端点 URL。")]
    public string? Url { get; set; }

    /// <summary>
    /// SSE 或 HTTP 传输使用的自定义请求头，使用 JSON 对象字符串持久化。
    /// </summary>
    [YamlMember(Alias = "headers_json", Description = "SSE 或 HTTP 传输使用的自定义请求头，使用 JSON 对象字符串持久化。")]
    public string? HeadersJson { get; set; }

    /// <summary>
    /// 指示该 MCP Server 当前是否启用。
    /// </summary>
    [YamlMember(Alias = "is_enabled", Description = "指示该 MCP Server 当前是否启用。")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 创建时间的 Unix 毫秒时间戳。
    /// </summary>
    [YamlMember(Alias = "created_at_ms", Description = "创建时间的 Unix 毫秒时间戳。")]
    public long CreatedAtMs { get; set; }

    /// <summary>
    /// 配置来源，0 表示手动创建，1 表示由插件注册。
    /// </summary>
    [YamlMember(Alias = "source", Description = "配置来源，0 表示手动创建，1 表示由插件注册。")]
    public int Source { get; set; }

    /// <summary>
    /// 来源插件 ID，仅在 Source 为插件时有效。
    /// </summary>
    [YamlMember(Alias = "plugin_id", Description = "来源插件 ID，仅在 Source 为插件时有效。")]
    public string? PluginId { get; set; }

    /// <summary>
    /// 来源插件名称，仅在 Source 为插件时有效。
    /// </summary>
    [YamlMember(Alias = "plugin_name", Description = "来源插件名称，仅在 Source 为插件时有效。")]
    public string? PluginName { get; set; }
}