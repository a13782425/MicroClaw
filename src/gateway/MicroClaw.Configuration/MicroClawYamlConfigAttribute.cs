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
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Gets or sets the header comment that should be emitted before the YAML document.
    /// </summary>
    public string? HeaderComment { get; set; }
    
    /// <summary>
    /// Gets or sets a value indicating whether the option type supports YAML write-back.
    /// </summary>
    public bool IsWritable { get; set; } = true;
}