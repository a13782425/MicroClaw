namespace MicroClaw.Configuration;

/// <summary>
/// Marker interface for option types managed by <see cref="MicroClawConfig"/>.
/// A valid option type must both implement this interface and declare
/// <see cref="MicroClawYamlConfigAttribute"/> metadata.
/// Types that also implement <see cref="IMicroClawConfigTemplate"/> can lazily
/// materialize a default YAML template when the backing file or section is missing.
/// </summary>
public interface IMicroClawConfigOptions
{
}