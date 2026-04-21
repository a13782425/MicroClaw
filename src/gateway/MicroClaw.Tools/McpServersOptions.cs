using Microsoft.Extensions.Configuration;
using MicroClaw.Configuration;
using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Tools;

/// <summary>
/// MCP Server 配置列表。
/// 通过 <c>mcp-servers.yaml</c> 持久化，通过 <see cref="MicroClawConfig.Get{T}"/> 读取，
/// 通过 <see cref="MicroClawConfig.Save{T}(T)"/> 写回。
/// </summary>
[MicroClawYamlConfig("mcp_servers", FileName = "mcp-servers.yaml", IsWritable = true)]
public sealed class McpServersOptions : IMicroClawConfigOptions
{
    [ConfigurationKeyName("items")]
    public List<McpServerConfigEntity> Items { get; set; } = [];
}