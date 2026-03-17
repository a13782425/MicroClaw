namespace MicroClaw.Commands;

/// <summary>
/// 各配置文件的默认内容，用于 init 命令和 serve 自动初始化。
/// </summary>
internal static class InitDefaults
{
    internal const string MicroclawYaml = """
        # MicroClaw 主配置文件
        # 通过 $imports 导入子配置文件，支持具体路径和通配符（如 ./config/*.yaml）
        # 规则：
        #   - 主配置作为默认值层，子配置覆盖主配置中的同名 key
        #   - 多个子配置文件之间不允许出现相同的 key（冲突时启动报错）

        $imports:
          - ./config/*.yaml
        """;

    internal const string AuthYaml = """
        # 认证配置
        # 敏感字段建议通过环境变量覆盖，例如：
        #   DOTNET_auth__password=your-password
        #   DOTNET_auth__jwt_secret=your-secret

        auth:
          username: "admin"
          password: "changeme"
          jwt_secret: "please-change-this-secret-key-min-32-chars!!"
          expires_hours: 8
        """;

    internal const string ChannelsYaml = """
        # 功能开关：启用的 Provider 和 Channel
        # 支持的 providers: openai, claude
        # 支持的 channels: feishu, wecom, wechat

        features:
          providers:
            - openai
            - claude
          channels:
            - feishu
            - wecom
            - wechat
        """;

    internal const string LoggingYaml = """
        # Serilog 日志配置
        # microsoft.extensions.ai 设为 debug 可查看 AI 请求/响应日志

        serilog:
          minimum_level:
            default: information
            override:
              microsoft.aspnetcore: warning
              microsoft.extensions.ai: debug
          write_to:
            - name: console
              args:
                output_template: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            - name: file
              args:
                path: "logs/microclaw-.log"
                rolling_interval: day
                retained_file_count_limit: 7
                output_template: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
          enrich:
            - from_log_context
            - with_machine_name
            - with_thread_id
        """;

    internal const string ProvidersYaml = """
        # Model Provider 配置
        # 通过 WebUI 或 API 添加 Provider 后，配置将保存在此文件中
        # 请勿直接提交包含真实 API Key 的此文件

        providers: []
        """;

    internal const string DotEnvExample = """
        # MicroClaw 环境变量示例
        # 复制为 .env 并填入实际值，服务启动时会自动加载

        # 工作目录（覆盖默认的 .microclaw/）
        # MICROCLAW_HOME=/path/to/.microclaw

        # 主配置文件路径（覆盖默认的 $MICROCLAW_HOME/microclaw.yaml）
        # MICROCLAW_CONFIG_FILE=/path/to/microclaw.yaml

        # 服务监听地址与端口（默认 localhost:5080）
        # GATEWAY_HOST=0.0.0.0
        # GATEWAY_PORT=5080

        # 覆盖认证配置（优先级高于 auth.yaml）
        # DOTNET_auth__username=admin
        # DOTNET_auth__password=your-password
        # DOTNET_auth__jwt_secret=your-secret-min-32-chars

        # Model Provider API Keys（优先级高于 providers.yaml 中的 api_key 字段）
        # OPENAI__APIKEY=sk-...
        # CLAUDE__APIKEY=sk-ant-...
        """;
}
