using MicroClaw.Configuration;
using YamlDotNet.Serialization;
namespace MicroClaw.Desktop.Config;
[MicroClawYamlConfig("logging", FileName = "logging.yaml")]
public sealed class LoggingOptions : IMicroClawConfigTemplate
{
    [YamlMember(Alias = "minimum_level")]
    public string MinimumLevel { get; set; } = "Information";
    [YamlMember(Alias = "file")]
    public LoggingFileOptions File { get; set; } = new();
    public IMicroClawConfigOptions CreateDefaultTemplate()
    {
        return new LoggingOptions();
    }
}
public sealed class LoggingFileOptions
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;
    
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "logs/microclaw-.log";
    
    [YamlMember(Alias = "rolling_interval")]
    public string RollingInterval { get; set; } = "Day";
    
    [YamlMember(Alias = "retain_days")]
    public int RetainDays { get; set; } = 7;
    
    [YamlMember(Alias = "output_template")]
    public string OutputTemplate { get; set; } =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
}