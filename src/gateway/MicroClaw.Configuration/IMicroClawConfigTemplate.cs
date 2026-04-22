namespace MicroClaw.Configuration;

/// <summary>
/// Provides a lazily materialized default template for an options type.
/// When <see cref="MicroClawConfig.Get{T}"/> detects that the backing YAML file
/// or configuration section is missing, the returned template instance is written
/// to disk and cached as the resolved options value.
/// </summary>
public interface IMicroClawConfigTemplate : IMicroClawConfigOptions
{
    /// <summary>
    /// Creates the default options instance that should be persisted when no
    /// file-backed configuration is available.
    /// </summary>
    IMicroClawConfigOptions CreateDefaultTemplate();
}