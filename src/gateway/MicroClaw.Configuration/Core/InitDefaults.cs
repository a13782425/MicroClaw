namespace MicroClaw.Configuration;

/// <summary>
/// 仍需由初始化器直接写入的默认内容。
/// </summary>
public static class InitDefaults
{
    public const string MicroclawYaml = """
        # MicroClaw 主配置文件
        # 通过 $imports 导入子配置文件，支持具体路径和通配符（如 ./config/*.yaml）
        # 规则：
        #   - 主配置作为默认值层，子配置覆盖主配置中的同名 key
        #   - 多个子配置文件之间不允许出现相同的 key（冲突时启动报错）

        $imports:
          - ./config/*.yaml
        """;

    public const string ProvidersYaml = """
        # Model Provider 配置
        # 通过 WebUI 或 API 添加 Provider 后，配置将保存在此文件中
        # 请勿直接提交包含真实 API Key 的此文件

        providers:
          items: []
        """;

    public const string AgentsYaml = """
        # Agent 实体配置
        # 通过 WebUI 或 API 管理 Agent 后，配置将保存在此文件中

        agents:
          sub_agent_max_depth: 3
          items: []
        """;

    public const string SessionsYaml = """
        # 会话元数据配置
        # 由系统自动管理，请勿手动编辑

        sessions:
          items: []
        """;

    public const string SkillsYaml = """
        # Skills 技能配置
        # 技能文件夹相对于工作目录（workspace/），或使用绝对路径

        skills:
          # 默认技能文件夹（新技能将写入此目录）
          default_folder: skills
          # 附加技能文件夹（只读，不会往这些目录创建新技能）
          # additional_folders:
          #   - /path/to/shared-skills
          # 允许技能 SKILL.md 中执行 !`command` shell 注入（生产环境建议关闭）
          allow_command_injection: false
        """;

    public const string SandboxYaml = """
        # 沙盒文件下载 Token 配置
        # token_expiry_minutes: 下载链接的有效期（分钟），默认 60 分钟

        sandbox:
          token_expiry_minutes: 60
        """;

    public const string EmotionYaml = """
        # 情绪行为配置
        # 控制 Agent 根据情绪状态切换推理模式（正常 / 探索 / 谨慎 / 休息）
        # 所有情绪值域：[0, 100]，判定优先级：谨慎 > 探索 > 休息 > 正常

        emotion:
          # ── 模式切换阈值 ──
          # 谨慎：警觉度 >= 阈值
          cautious_alertness_threshold: 70
          # 谨慎：信心 <= 阈值
          cautious_confidence_threshold: 30
          # 探索：好奇心 >= 阈值
          explore_min_curiosity: 70
          # 探索：心情 >= 阈值
          explore_min_mood: 60
          # 休息：警觉度 <= 阈值
          rest_max_alertness: 30
          # 休息：心情 <= 阈值
          rest_max_mood: 40

          # ── 各模式推理参数（null 表示使用模型默认值）──
          # normal_temperature:
          # normal_top_p:
          # normal_system_prompt_suffix:
          # explore_temperature: 1.2
          # explore_top_p: 0.95
          # explore_system_prompt_suffix: "请发挥创造力，尝试新思路。"
          # cautious_temperature: 0.3
          # cautious_top_p: 0.5
          # cautious_system_prompt_suffix: "请保持审慎，优先确保准确性。"
          # rest_temperature: 0.5
          # rest_top_p: 0.7
          # rest_system_prompt_suffix:

          # ── 事件加减分（正数=加，负数=减，省略该维度=不变）──
          # 消息发送成功
          # delta_message_success: { mood: 3, confidence: 2 }
          # 消息发送失败
          # delta_message_failed: { alertness: 8, mood: -5, confidence: -5 }
          # Tool 执行成功
          # delta_tool_success: { curiosity: 2, confidence: 3 }
          # Tool 执行报错
          # delta_tool_error: { alertness: 10, mood: -3, confidence: -5 }
          # 用户满意
          # delta_user_satisfied: { mood: 10, confidence: 5 }
          # 用户不满意
          # delta_user_dissatisfied: { mood: -10, confidence: -5, alertness: 5 }
          # 任务完成
          # delta_task_completed: { mood: 8, confidence: 8, alertness: -5 }
          # 任务失败
          # delta_task_failed: { alertness: 10, mood: -8, confidence: -8 }
          # 高严重度痛觉
          # delta_pain_high: { alertness: 22, mood: -5, confidence: -18 }
          # 极高严重度痛觉
          # delta_pain_critical: { alertness: 32, mood: -10, confidence: -28 }
        """;

    public const string DotEnvExample = """
        # MicroClaw 环境变量示例
        # 复制为 .env 并填入实际值，服务启动时会自动加载

        # 工作目录需在启动进程前通过外部环境变量设置，不能写在 .env 中
        # 示例（PowerShell）：$env:MICROCLAW_HOME = "/path/to/.microclaw"

        # 主配置文件路径也需在启动进程前通过外部环境变量设置，不能写在 .env 中
        # 示例（PowerShell）：$env:MICROCLAW_CONFIG_FILE = "/path/to/microclaw.yaml"

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
