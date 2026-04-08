using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
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
///   <item>创建 <see cref="PetContext"/> 并挂载到 Session（通过 <see cref="Session.AttachPet"/>）</item>
/// </list>
/// </para>
/// </summary>
public sealed class PetFactory(
    PetStateStore stateStore,
    PetContextFactory contextFactory,
    MicroClawConfigEnv env,
    ILogger<PetFactory> logger)
    : IPetFactory
{
    private readonly PetStateStore _stateStore = stateStore;
    private readonly PetContextFactory _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly string _sessionsDir = env.SessionsDir;
    private readonly ILogger<PetFactory> _logger = logger;

    internal PetFactory(
        PetStateStore stateStore,
        PetContextFactory contextFactory,
        string sessionsDir,
        ILogger<PetFactory> logger) : this(
            stateStore,
            contextFactory,
            CreateTestEnv(sessionsDir),
            logger)
    {
    }

    /// <summary>
    /// Creates or loads the runtime Pet for the specified Session.
    /// </summary>
    /// <param name="session">Session runtime contract.</param>
    /// <param name="config">Pet 配置（可选，不提供则使用默认值）。</param>
    /// <param name="ct">取消令牌。</param>
    public Task<IPet?> CreateOrLoadAsync(ISession session, CancellationToken ct = default)
        => CreateOrLoadAsync(session, config: null, ct);

    public async Task<IPet?> CreateOrLoadAsync(ISession session, PetConfig? config = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Id);

        if (session.ParentSessionId is not null)
        {
            _logger.LogDebug("子会话不创建独立 Pet：SessionId={SessionId}", session.Id);
            return session.Pet;
        }

        string sessionId = session.Id;
        string petDir = Path.Combine(_sessionsDir, sessionId, "pet");

        // 幂等：目录已存在时跳过
        if (!Directory.Exists(petDir))
        {
            Directory.CreateDirectory(petDir);

            PetState initialState = new()
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

            string configFile = Path.Combine(petDir, "config.json");
            PetConfig petConfig = config ?? new PetConfig();
            string configJson = System.Text.Json.JsonSerializer.Serialize(petConfig,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configFile, configJson, ct);

            await WriteDefaultYamlAsync(Path.Combine(petDir, "personality.yaml"), DefaultPersonalityYaml, ct);
            await WriteDefaultYamlAsync(Path.Combine(petDir, "dispatch-rules.yaml"), DefaultDispatchRulesYaml, ct);
            await WriteDefaultYamlAsync(Path.Combine(petDir, "knowledge-interests.yaml"), DefaultKnowledgeInterestsYaml, ct);

            _logger.LogInformation("Pet 初始化完成：SessionId={SessionId}", sessionId);
        }

        PetContext? petCtx = await _contextFactory.LoadAsync(session, ct);
        if (petCtx is null)
            return null;

        if (session.IsApproved)
            petCtx.Activate();
        return petCtx;
    }

    /// <summary>
    /// Activates the runtime Pet for an approved Session.
    /// </summary>
    public async Task<IPet?> ActivateAsync(ISession session, CancellationToken ct = default)
    {
        IPet? pet = await CreateOrLoadAsync(session, ct);
        if (pet is PetContext petContext)
            petContext.Activate();
        return pet;
    }

    private static MicroClawConfigEnv CreateTestEnv(string sessionsDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionsDir);
        string rootDir = Directory.GetParent(Directory.GetParent(sessionsDir)!.FullName)!.FullName;
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", rootDir);
        return MicroClawConfig.Env;
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
