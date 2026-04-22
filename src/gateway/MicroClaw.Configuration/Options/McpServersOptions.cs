using YamlDotNet.Serialization;

namespace MicroClaw.Configuration.Options;

/// <summary>
/// MCP Server 配置列表。
/// 通过 <c>mcp-servers.yaml</c> 持久化，通过 <see cref="MicroClawConfig.Get{T}"/> 读取，
/// 通过 <see cref="MicroClawConfig.Save{T}(T)"/> 写回。
/// </summary>
[MicroClawYamlConfig("mcp_servers", FileName = "mcp-servers.yaml", IsWritable = true)]
public sealed class McpServersOptions : IMicroClawConfigTemplate
{
    /// <summary>
    /// 当前系统中持久化的 MCP Server 配置列表。
    /// </summary>
    [YamlMember(Alias = "items", Description = "当前系统中持久化的 MCP Server 配置列表。")]
    public List<McpServerConfigEntity> Items { get; set; } = [];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new McpServersOptions();
}