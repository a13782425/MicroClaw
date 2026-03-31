namespace MicroClaw.Gateway.Contracts.Plugins;

/// <summary>
/// Allows the plugin system to dynamically register / remove skill root directories.
/// Implemented by SkillService; consumed by PluginLoader.
/// </summary>
public interface IPluginSkillRegistrar
{
    /// <summary>Add a skill root directory (idempotent — duplicate paths are ignored).</summary>
    void AddRoot(string path);

    /// <summary>Remove a previously added skill root directory.</summary>
    void RemoveRoot(string path);
}
