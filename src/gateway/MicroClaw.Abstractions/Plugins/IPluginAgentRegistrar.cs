namespace MicroClaw.Abstractions.Plugins;

/// <summary>
/// Allows the plugin system to import / remove Agent definitions from plugin files.
/// Implemented by AgentStore; consumed by PluginLoader.
/// </summary>
public interface IPluginAgentRegistrar
{
    /// <summary>
    /// Import an Agent definition from a Markdown file with YAML front-matter.
    /// The agent is tagged with <c>plugin:{pluginName}</c> as its source for later bulk removal.
    /// </summary>
    Task ImportFromFileAsync(string filePath, string pluginName, CancellationToken ct = default);

    /// <summary>
    /// Remove all agents that were imported by the specified plugin.
    /// </summary>
    Task RemoveByPluginAsync(string pluginName, CancellationToken ct = default);
}
