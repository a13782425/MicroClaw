using MicroClaw.Configuration;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Storage;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 工厂：在 Session 审批时自动为新 Session 创建 Pet。
/// <para>
/// 职责：
/// <list type="bullet">
///   <item>初始化 <c>{sessionId}/pet/</c> 目录结构</item>
///   <item>写入默认 <c>state.json</c>（Idle 状态）</item>
///   <item>复制默认 YAML 提示词模板（personality.yaml / dispatch-rules.yaml / knowledge-interests.yaml）</item>
///   <item>写入默认 <c>config.json</c>（PetConfig 默认值）</item>
/// </list>
/// </para>
/// </summary>
public sealed class PetFactory(
    PetStateStore stateStore,
    MicroClawConfigEnv env,
    ILogger<PetFactory> logger)
{
    private readonly PetStateStore _stateStore = stateStore;
    private readonly string _sessionsDir = env.SessionsDir;
    private readonly ILogger<PetFactory> _logger = logger;

    /// <summary>
    /// 为指定 Session 创建 Pet，若 Pet 目录已存在则跳过。
    /// </summary>
    /// <param name="sessionId">Session 唯一标识符。</param>
    /// <param name="config">Pet 配置（可选，不提供则使用默认值）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task CreateAsync(string sessionId, PetConfig? config = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        string petDir = Path.Combine(_sessionsDir, sessionId, "pet");

        // 幂等：目录已存在时跳过
        if (Directory.Exists(petDir))
        {
            _logger.LogDebug("Pet 目录已存在，跳过初始化：SessionId={SessionId}", sessionId);
            return;
        }

        Directory.CreateDirectory(petDir);

        // 1. 写入初始 PetState（Idle，默认情绪）
        var initialState = new PetState
        {
            SessionId = sessionId,
            BehaviorState = PetBehaviorState.Idle,
            EmotionState = EmotionState.Default,
            LlmCallCount = 0,
            WindowStart = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _stateStore.SaveAsync(initialState, ct);

        // 2. 写入默认 PetConfig
        string configFile = Path.Combine(petDir, "config.json");
        var petConfig = config ?? new PetConfig();
        string configJson = System.Text.Json.JsonSerializer.Serialize(petConfig,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(configFile, configJson, ct);

        // 3. 写入默认 YAML 提示词模板
        await WriteDefaultYamlAsync(Path.Combine(petDir, "personality.yaml"), DefaultPersonalityYaml, ct);
        await WriteDefaultYamlAsync(Path.Combine(petDir, "dispatch-rules.yaml"), DefaultDispatchRulesYaml, ct);
        await WriteDefaultYamlAsync(Path.Combine(petDir, "knowledge-interests.yaml"), DefaultKnowledgeInterestsYaml, ct);

        _logger.LogInformation("Pet 初始化完成：SessionId={SessionId}", sessionId);
    }

    private static async Task WriteDefaultYamlAsync(string path, string content, CancellationToken ct)
    {
        if (!File.Exists(path))
            await File.WriteAllTextAsync(path, content, ct);
    }

    // ── 默认 YAML 模板 ──────────────────────────────────────────────────────

    private const string DefaultPersonalityYaml = """
# Pet 人格提示词
# 此文件由 PetFactory 自动生成，可手动编辑或由 Pet 自主进化。

persona: |
  你是一个智能会话助理（Pet），负责理解用户需求，选择合适的 Agent 和工具执行任务。
  你善于学习用户习惯，在对话中积累知识，并随着时间不断改进自己的工作方式。

tone: professional
language: zh-cn
""";

    private const string DefaultDispatchRulesYaml = """
# Pet 调度规则
# 控制 Pet 如何将用户消息分配给 Agent 和 Provider。

default_strategy: default

rules:
  - pattern: ".*代码.*|.*编程.*|.*bug.*"
    preferred_model_type: quality
    notes: "代码相关问题优先使用高质量模型"

  - pattern: ".*翻译.*|.*简单.*"
    preferred_model_type: cost
    notes: "简单任务使用低成本模型"
""";

    private const string DefaultKnowledgeInterestsYaml = """
# Pet 学习方向
# 定义 Pet 在学习状态下关注哪些类型的知识。

topics:
  - name: user_preferences
    description: 用户偏好与工作习惯
    priority: high

  - name: domain_knowledge
    description: 会话涉及的领域知识
    priority: medium

  - name: error_patterns
    description: 失败模式和错误处理经验
    priority: medium
""";
}
