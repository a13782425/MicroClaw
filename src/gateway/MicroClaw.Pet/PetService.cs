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
    private readonly PetStateStore _stateStore;
    private readonly IEmotionStore _emotionStore;
    private readonly string _sessionsDir;
    private readonly ILogger<PetService> _logger;
    
    public PetService()
    {
        _stateStore = MicroEngine.Instance.GetRequiredService<PetStateStore>();
        _emotionStore = MicroEngine.Instance.GetRequiredService<IEmotionStore>();
        _sessionsDir = MicroClawConfig.Env.SessionsDir;
        _logger = MicroEngine.Instance.GetRequiredService<ILogger<PetService>>();
    }
    
    public override int Order => 25;
    
    /// <summary>
    /// 为指定 Session 创建或加载运行时 Pet。
    /// </summary>
    public virtual async Task<IPet?> CreateOrLoadAsync(IMicroSession session, CancellationToken ct = default)
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
            
            PetConfig petConfig = new PetConfig();
            await _stateStore.SaveConfigAsync(sessionId, petConfig, ct);
            
            await WriteDefaultYamlAsync(Path.Combine(petDir, "personality.yaml"), DefaultPersonalityYaml, ct);
            await WriteDefaultYamlAsync(Path.Combine(petDir, "dispatch-rules.yaml"), DefaultDispatchRulesYaml, ct);
            await WriteDefaultYamlAsync(Path.Combine(petDir, "knowledge-interests.yaml"), DefaultKnowledgeInterestsYaml, ct);
            
            _logger.LogInformation("Pet 初始化完成：SessionId={SessionId}", sessionId);
        }
        
        IPet? existingPet = session.Pet;
        if (existingPet is not null and not MicroPet)
            return existingPet;
        
        MicroPet? petContext = existingPet as MicroPet;
        if (petContext is null || petContext.IsDisposed)
            petContext = await LoadContextAsync(session, ct);
        
        if (petContext is null)
            return null;
        
        if (session.IsApproved)
            await ActivatePetAsync(petContext, ct);
        else
            await DeactivatePetAsync(petContext, ct);
        
        return petContext;
    }
    
    /// <summary>
    /// 激活指定 Session 的运行时 Pet。
    /// </summary>
    public virtual async Task<IPet?> ActivateAsync(IMicroSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        
        IPet? pet = session.Pet ?? await CreateOrLoadAsync(session, ct);
        if (!session.IsApproved)
            return pet;
        
        if (pet is MicroPet petContext)
            await ActivatePetAsync(petContext, ct);
        
        return pet;
    }
    
    /// <summary>
    /// 停用指定 Session 的运行时 Pet。
    /// </summary>
    public virtual async Task<IPet?> DeactivateAsync(IMicroSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        
        IPet? pet = session.Pet;
        if (pet is MicroPet petContext)
            await DeactivatePetAsync(petContext, ct);
        
        return pet;
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
        PetContextState initialState = PetContextState.Disabled;
        
        var pet = new MicroPet(session, petState, petConfig, emotion, initialState);
        await AttachDefaultComponentsAsync(pet, ct);
        return pet;
    }
    
    private async ValueTask ActivatePetAsync(MicroPet pet, CancellationToken ct)
    {
        MicroEngine? engine = Engine;
        if (engine is null)
        {
            pet.Activate();
            return;
        }
        
        if (ReferenceEquals(pet.Engine, engine))
        {
            pet.Activate();
            return;
        }
        
        if (engine.State is MicroEngineState.Starting or MicroEngineState.Stopping)
        {
            _logger.LogDebug("Pet 注册延后：SessionId={SessionId}, EngineState={EngineState}", pet.MicroSession.Id, engine.State);
            return;
        }
        
        await engine.RegisterObjectAsync(pet, ct);
        pet.Activate();
    }
    
    private async ValueTask DeactivatePetAsync(MicroPet pet, CancellationToken ct)
    {
        pet.Disable();
        
        MicroEngine? engine = Engine;
        if (engine is not null && ReferenceEquals(pet.Engine, engine))
            await engine.UnregisterObjectAsync(pet, ct);
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
    
    private static async ValueTask EnsureComponentAsync<TComponent>(MicroPet pet, CancellationToken ct) where TComponent : PetComponent, new()
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