namespace MicroClaw.Configuration;

[MicroClawYamlConfig("serilog", FileName = "logging.yaml")]
public sealed class LoggingOptions : IMicroClawConfigTemplate
{
    [YamlMember(Alias = "minimum_level")]
    public LoggingMinimumLevelOptions MinimumLevel { get; set; } = new();

    [YamlMember(Alias = "write_to")]
    public List<LoggingSinkOptions> WriteTo { get; set; } = [.. CreateDefaultSinks()];

    [YamlMember(Alias = "enrich")]
    public List<string> Enrich { get; set; } = [.. CreateDefaultEnrichers()];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new LoggingOptions();

    public static IReadOnlyList<LoggingSinkOptions> CreateDefaultSinks() =>
    [
        new LoggingSinkOptions
        {
            Name = "console",
            Args = new LoggingSinkArgsOptions
            {
                OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            }
        },
        new LoggingSinkOptions
        {
            Name = "file",
            Args = new LoggingSinkArgsOptions
            {
                Path = "logs/microclaw-.log",
                RollingInterval = "day",
                RetainedFileCountLimit = 7,
                OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
            }
        }
    ];

    public static IReadOnlyList<string> CreateDefaultEnrichers() =>
    [
        "from_log_context",
        "with_machine_name",
        "with_thread_id",
    ];
}

public sealed class LoggingMinimumLevelOptions
{
    [YamlMember(Alias = "default")]
    public string Default { get; set; } = "Information";

    [YamlMember(Alias = "override")]
    public LoggingOverrideOptions Override { get; set; } = new();
}

public sealed class LoggingOverrideOptions
{
    [YamlMember(Alias = "microsoft_aspnetcore")]
    public string MicrosoftAspNetCore { get; set; } = "Warning";

    [YamlMember(Alias = "microsoft_extensions_ai")]
    public string MicrosoftExtensionsAi { get; set; } = "Debug";

    [YamlMember(Alias = "microsoft_entity_framework_core_database_command")]
    public string MicrosoftEntityFrameworkCoreDatabaseCommand { get; set; } = "Warning";
}

public sealed class LoggingSinkOptions
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "args")]
    public LoggingSinkArgsOptions Args { get; set; } = new();
}

public sealed class LoggingSinkArgsOptions
{
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "rolling_interval")]
    public string? RollingInterval { get; set; }

    [YamlMember(Alias = "retained_file_count_limit")]
    public int? RetainedFileCountLimit { get; set; }

    [YamlMember(Alias = "output_template")]
    public string? OutputTemplate { get; set; }
}