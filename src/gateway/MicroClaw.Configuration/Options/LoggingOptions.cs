namespace MicroClaw.Configuration;

/// <summary>
/// Serilog 日志配置选项，控制最低级别、输出目标和增强器。
/// </summary>
[MicroClawYamlConfig("serilog", FileName = "logging.yaml")]
public sealed class LoggingOptions : IMicroClawConfigTemplate
{
    /// <summary>
    /// 日志最低级别及各命名空间覆盖规则。
    /// </summary>
    [YamlMember(Alias = "minimum_level", Description = "日志最低级别及各命名空间覆盖规则。")]
    public LoggingMinimumLevelOptions MinimumLevel { get; set; } = new();

    /// <summary>
    /// 日志输出目标列表，例如控制台和文件。
    /// </summary>
    [YamlMember(Alias = "write_to", Description = "日志输出目标列表，例如控制台和文件。")]
    public List<LoggingSinkOptions> WriteTo { get; set; } = [.. CreateDefaultSinks()];

    /// <summary>
    /// 启用的日志增强器列表。
    /// </summary>
    [YamlMember(Alias = "enrich", Description = "启用的日志增强器列表。")]
    public List<string> Enrich { get; set; } = [.. CreateDefaultEnrichers()];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new LoggingOptions();

    /// <summary>
    /// 创建默认日志输出目标配置。
    /// </summary>
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

    /// <summary>
    /// 创建默认日志增强器配置。
    /// </summary>
    public static IReadOnlyList<string> CreateDefaultEnrichers() =>
    [
        "from_log_context",
        "with_machine_name",
        "with_thread_id",
    ];
}

/// <summary>
/// 日志最低级别配置。
/// </summary>
public sealed class LoggingMinimumLevelOptions
{
    /// <summary>
    /// 全局默认日志级别。
    /// </summary>
    [YamlMember(Alias = "default", Description = "全局默认日志级别。")]
    public string Default { get; set; } = "Information";

    /// <summary>
    /// 针对特定命名空间的日志级别覆盖规则。
    /// </summary>
    [YamlMember(Alias = "override", Description = "针对特定命名空间的日志级别覆盖规则。")]
    public LoggingOverrideOptions Override { get; set; } = new();
}

/// <summary>
/// 常见框架命名空间的日志级别覆盖配置。
/// </summary>
public sealed class LoggingOverrideOptions
{
    /// <summary>
    /// Microsoft.AspNetCore 命名空间的日志级别。
    /// </summary>
    [YamlMember(Alias = "microsoft_aspnetcore", Description = "Microsoft.AspNetCore 命名空间的日志级别。")]
    public string MicrosoftAspNetCore { get; set; } = "Warning";

    /// <summary>
    /// Microsoft.Extensions.AI 命名空间的日志级别。
    /// </summary>
    [YamlMember(Alias = "microsoft_extensions_ai", Description = "Microsoft.Extensions.AI 命名空间的日志级别。")]
    public string MicrosoftExtensionsAi { get; set; } = "Debug";

    /// <summary>
    /// EF Core 数据库命令日志的级别。
    /// </summary>
    [YamlMember(Alias = "microsoft_entity_framework_core_database_command", Description = "EF Core 数据库命令日志的级别。")]
    public string MicrosoftEntityFrameworkCoreDatabaseCommand { get; set; } = "Warning";
}

/// <summary>
/// 单个日志输出目标的配置。
/// </summary>
public sealed class LoggingSinkOptions
{
    /// <summary>
    /// 输出目标名称，例如 console 或 file。
    /// </summary>
    [YamlMember(Alias = "name", Description = "输出目标名称，例如 console 或 file。")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 输出目标的参数配置。
    /// </summary>
    [YamlMember(Alias = "args", Description = "输出目标的参数配置。")]
    public LoggingSinkArgsOptions Args { get; set; } = new();
}

/// <summary>
/// 日志输出目标的参数明细。
/// </summary>
public sealed class LoggingSinkArgsOptions
{
    /// <summary>
    /// 文件输出路径；控制台输出通常为空。
    /// </summary>
    [YamlMember(Alias = "path", Description = "文件输出路径；控制台输出通常为空。")]
    public string? Path { get; set; }

    /// <summary>
    /// 文件滚动周期，例如 day。
    /// </summary>
    [YamlMember(Alias = "rolling_interval", Description = "文件滚动周期，例如 day。")]
    public string? RollingInterval { get; set; }

    /// <summary>
    /// 保留的历史日志文件数量上限。
    /// </summary>
    [YamlMember(Alias = "retained_file_count_limit", Description = "保留的历史日志文件数量上限。")]
    public int? RetainedFileCountLimit { get; set; }

    /// <summary>
    /// 输出模板字符串。
    /// </summary>
    [YamlMember(Alias = "output_template", Description = "输出模板字符串。")]
    public string? OutputTemplate { get; set; }
}