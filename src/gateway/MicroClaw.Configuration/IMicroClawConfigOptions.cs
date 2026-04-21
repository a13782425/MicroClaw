namespace MicroClaw.Configuration;

/// <summary>
/// Marker interface for option types managed by <see cref="MicroClawConfig"/>.
/// A valid option type must both implement this interface and declare
/// <see cref="MicroClawYamlConfigAttribute"/> metadata.
/// </summary>
public interface IMicroClawConfigOptions
{
}