namespace MicroClaw.Configuration;

/// <summary>
/// Declares YAML binding metadata for an option type managed by
/// <see cref="MicroClawConfig"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MicroClawYamlConfigAttribute(string sectionKey) : Attribute
{
    /// <summary>
    /// Gets the configuration section key used for binding.
    /// </summary>
    public string SectionKey { get; } = sectionKey;

    /// <summary>
    /// Gets or sets the YAML file name used by <see cref="MicroClawConfig.Save{T}(T)"/>.
    /// When omitted, YAML write-back is disabled unless a concrete file name is declared.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Gets or sets the directory path used for YAML write-back and template materialization.
    /// Relative paths are resolved from the MicroClaw HOME root during normal startup.
    /// When omitted, <see cref="MicroClawConfig"/> falls back to its configured config directory.
    /// </summary>
    public string? DirectoryPath { get; set; }

    /// <summary>
    /// Gets or sets the header comment that should be emitted before the YAML document.
    /// </summary>
    public string? HeaderComment { get; set; }
    
    /// <summary>
    /// Gets or sets a value indicating whether the option type supports YAML write-back.
    /// Effective write-back still requires an explicit <see cref="FileName"/>.
    /// </summary>
    public bool IsWritable { get; set; } = true;
}