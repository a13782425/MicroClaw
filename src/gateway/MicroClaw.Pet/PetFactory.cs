using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Storage;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;

/// <summary>
/// Creates or loads the runtime Pet resources for a persisted session.
/// </summary>
public sealed class PetFactory : IPetFactory
{
    private readonly PetStateStore _stateStore;
    private readonly PetContextFactory _contextFactory;
    private readonly string _sessionsDir;
    private readonly ILogger<PetFactory> _logger;

    public PetFactory(PetStateStore stateStore, PetContextFactory contextFactory, ILogger<PetFactory> logger)
    {
        _stateStore = stateStore;
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _sessionsDir = MicroClawConfig.Env.SessionsDir;
        _logger = logger;
    }

    internal PetFactory(
        PetStateStore stateStore,
        PetContextFactory contextFactory,
        string sessionsDir,
        ILogger<PetFactory> logger)
    {
        _stateStore = stateStore;
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _sessionsDir = sessionsDir;
        _logger = logger;
    }

    /// <summary>
    /// Creates or loads the runtime Pet for the specified session.
    /// </summary>
    public Task<IPet?> CreateOrLoadAsync(IMicroSession microSession, CancellationToken ct = default)
        => CreateOrLoadAsync(microSession, config: null, ct);

    public async Task<IPet?> CreateOrLoadAsync(IMicroSession microSession, PetConfig? config = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(microSession);
        ArgumentException.ThrowIfNullOrWhiteSpace(microSession.Id);

        string sessionId = microSession.Id;
        string petDir = Path.Combine(_sessionsDir, sessionId, "pet");

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

        MicroPet? petCtx = await _contextFactory.LoadAsync(microSession, ct);
        if (petCtx is null)
            return null;

        if (microSession.IsApproved)
            petCtx.Activate();
        return petCtx;
    }

    /// <summary>
    /// Activates the runtime Pet for an approved session.
    /// </summary>
    public async Task<IPet?> ActivateAsync(IMicroSession microSession, CancellationToken ct = default)
    {
        IPet? pet = await CreateOrLoadAsync(microSession, ct);
        if (pet is MicroPet petContext)
            petContext.Activate();
        return pet;
    }

    private static async Task WriteDefaultYamlAsync(string path, string content, CancellationToken ct)
    {
        if (!File.Exists(path))
            await File.WriteAllTextAsync(path, content, ct);
    }

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
