using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration;
using MicroClaw.Core;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Storage;
using MicroClaw.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;
/// <summary>
/// Pet 服务层：负责 Pet 的首次初始化、惰性加载与激活。
/// </summary>
public class PetService : MicroService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PetStateStore _stateStore;
    private readonly IEmotionStore _emotionStore;
    private readonly string _sessionsDir;
    private readonly ILogger<PetService> _logger;

    public PetService(IServiceProvider sp)
    {
        _serviceProvider = sp ?? throw new ArgumentNullException(nameof(sp));
        _stateStore = sp.GetRequiredService<PetStateStore>();
        _emotionStore = sp.GetRequiredService<IEmotionStore>();
        _sessionsDir = MicroClawConfig.Env.SessionsDir;
        _logger = sp.GetRequiredService<ILogger<PetService>>();
    }

    public override int Order => 25;

    /// <summary>
    /// 为指定 Session 创建或加载运行时 Pet。
    /// </summary>
    public virtual Task<IPet?> CreateOrLoadAsync(IMicroSession session, CancellationToken ct = default) => CreateOrLoadAsync(session, config: null, ct);

    /// <summary>
    /// 为指定 Session 创建或加载运行时 Pet，并可指定初始配置。
    /// </summary>
    public virtual async Task<IPet?> CreateOrLoadAsync(IMicroSession session, PetConfig? config = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Id);

        string sessionId = session.Id;
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
                WindowStart = TimeUtils.NowOffset(),
                CreatedAt = TimeUtils.NowOffset(),
                UpdatedAt = TimeUtils.NowOffset()
            };
            await _stateStore.SaveAsync(initialState, ct);

            PetConfig petConfig = config ?? new PetConfig();
            await _stateStore.SaveConfigAsync(sessionId, petConfig, ct);

            await WriteDefaultYamlAsync(Path.Combine(petDir, "personality.yaml"), DefaultPersonalityYaml, ct);
            await WriteDefaultYamlAsync(Path.Combine(petDir, "dispatch-rules.yaml"), DefaultDispatchRulesYaml, ct);
            await WriteDefaultYamlAsync(Path.Combine(petDir, "knowledge-interests.yaml"), DefaultKnowledgeInterestsYaml, ct);

            _logger.LogInformation("Pet 初始化完成：SessionId={SessionId}", sessionId);
        }

        MicroPet? petContext = await LoadContextAsync(session, ct);
        if (petContext is null)
            return null;

        if (session.IsApproved)
            await petContext.ActivatePetAsync(ct);

        return petContext;
    }

    /// <summary>
    /// 激活指定 Session 的运行时 Pet。
    /// </summary>
    public virtual async Task<IPet?> ActivateAsync(IMicroSession session, CancellationToken ct = default)
    {
        IPet? pet = await CreateOrLoadAsync(session, ct);
        if (pet is MicroPet petContext)
            await petContext.ActivatePetAsync(ct);

        return pet;
    }

    /// <summary>
    /// 获取当前 Session 的 Pet；若尚未挂载则按审批状态执行惰性加载。
    /// </summary>
    public virtual async Task<IPet?> GetOrLoadPetAsync(IMicroSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        IPet? pet = session.Pet;
        if (pet is not null)
            return pet;

        if (!session.IsApproved)
            return null;

        return await LoadContextAsync(session, ct);
    }

    private async Task<MicroPet?> LoadContextAsync(IMicroSession session, CancellationToken ct = default)
    {
        PetState? petState = await _stateStore.LoadAsync(session.Id, ct);
        if (petState is null)
            return null;

        PetConfig? petConfig = await _stateStore.LoadConfigAsync(session.Id, ct);
        if (petConfig is null)
            return null;

        EmotionState emotion = await _emotionStore.GetCurrentAsync(session.Id, ct);
        PetContextState initialState = session.IsApproved ? PetContextState.Active : PetContextState.Disabled;

        var pet = new MicroPet(_serviceProvider, session, petState, petConfig, emotion, initialState);
        await AttachDefaultComponentsAsync(pet, ct);
        await pet.AlignLifecycleToStateAsync(ct);
        return pet;
    }

    private static async ValueTask AttachDefaultComponentsAsync(MicroPet pet, CancellationToken ct)
    {
        await EnsureComponentAsync<PetStateComponent>(pet, ct);
        await EnsureComponentAsync<PetEmotionComponent>(pet, ct);
        await EnsureComponentAsync<PetPromptComponent>(pet, ct);
        await EnsureComponentAsync<PetRateLimitComponent>(pet, ct);
        await EnsureComponentAsync<PetKnowledgeComponent>(pet, ct);
        await EnsureComponentAsync<PetNotificationComponent>(pet, ct);
        await EnsureComponentAsync<PetDecisionComponent>(pet, ct);
        await EnsureComponentAsync<PetDispatchLifecycleComponent>(pet, ct);
        await EnsureComponentAsync<PetObservationComponent>(pet, ct);
        await EnsureComponentAsync<PetActionComponent>(pet, ct);
        await EnsureComponentAsync<PetHeartbeatComponent>(pet, ct);
    }

    private static async ValueTask EnsureComponentAsync<TComponent>(MicroPet pet, CancellationToken ct)
        where TComponent : PetComponent, new()
    {
        if (pet.GetComponent<TComponent>() is null)
            await pet.AddComponentAsync<TComponent>(ct);
    }

    private static async Task WriteDefaultYamlAsync(string path, string content, CancellationToken ct)
    {
        if (!File.Exists(path))
            await File.WriteAllTextAsync(path, content, ct);
    }

    private const string DefaultPersonalityYaml = """
                                                  # Pet 人格提示词
                                                  # 此文件由 PetService 自动生成，可手动编辑或由 Pet 自主进化。

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