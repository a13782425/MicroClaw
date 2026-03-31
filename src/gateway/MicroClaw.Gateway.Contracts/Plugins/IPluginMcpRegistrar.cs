namespace MicroClaw.Gateway.Contracts.Plugins;

/// <summary>
/// Allows the plugin system to register / unregister MCP servers from a <c>.mcp.json</c> config file.
/// Implemented by McpServerRegistry; consumed by PluginLoader.
/// </summary>
public interface IPluginMcpRegistrar
{
    /// <summary>
    /// Parse a <c>.mcp.json</c> file and register all MCP servers it defines.
    /// Server IDs are prefixed with <c>plugin:{pluginName}:</c> for source tracking.
    /// </summary>
    Task RegisterFromConfigFileAsync(string filePath, string pluginName, CancellationToken ct = default);

    /// <summary>
    /// Unregister all MCP servers that were registered by the specified plugin.
    /// </summary>
    void UnregisterByPlugin(string pluginName);
}
