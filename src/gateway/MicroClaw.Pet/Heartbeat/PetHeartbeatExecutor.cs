using MicroClaw.Pet.Rag;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet.Heartbeat;

/// <summary>
/// 单个 Pet 的心跳执行逻辑。
/// <para>
/// 由 <see cref="MicroClaw.Jobs"/> 中的 PetHeartbeatJob 对每个活跃 Session 调用。
/// 流程：
/// <list type="number">
///   <item>加载 PetState + PetConfig，校验 Pet 已启用</item>
///   <item>检查活跃时段（ActiveHoursStart/End），不在时段内则跳过</item>
///   <item>构建 <see cref="PetSelfAwarenessReport"/>（含 RAG 分块数）</item>
///   <item>调用 <see cref="PetStateMachine.EvaluateAsync"/>（LLM 驱动状态决策）</item>
///   <item>执行 <see cref="PetStateMachineDecision.PlannedActions"/>（通过 <see cref="PetActionExecutor"/>）</item>
///   <item>更新 LastHeartbeatAt 时间戳</item>
/// </list>
/// </para>
/// </summary>
public sealed class PetHeartbeatExecutor
{
    private readonly PetStateStore _stateStore;
    private readonly PetStateMachine _stateMachine;
    private readonly PetSelfAwarenessReportBuilder _reportBuilder;
    private readonly PetRagScope _petRagScope;
    private readonly PetActionExecutor _actionExecutor;
    private readonly IPetNotifier _petNotifier;
    private readonly ILogger<PetHeartbeatExecutor> _logger;

    public PetHeartbeatExecutor(IServiceProvider sp)
    {
        _stateStore = sp.GetRequiredService<PetStateStore>();
        _stateMachine = sp.GetRequiredService<PetStateMachine>();
        _reportBuilder = sp.GetRequiredService<PetSelfAwarenessReportBuilder>();
        _petRagScope = sp.GetRequiredService<PetRagScope>();
        _actionExecutor = sp.GetRequiredService<PetActionExecutor>();
        _petNotifier = sp.GetRequiredService<IPetNotifier>();
        _logger = sp.GetRequiredService<ILogger<PetHeartbeatExecutor>>();
    }

    /// <summary>
    /// 对指定 Session 的 Pet 执行一次心跳。
    /// </summary>
    /// <param name="sessionId">Session ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>心跳是否成功执行（跳过也视为成功）。</returns>
    public async Task<HeartbeatResult> ExecuteAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // ── 1. 加载 Pet 状态与配置 ──
        var petState = await _stateStore.LoadAsync(sessionId, ct);
        var petConfig = await _stateStore.LoadConfigAsync(sessionId, ct);

        if (petState is null || petConfig is not { Enabled: true })
        {
            _logger.LogDebug("Pet [{SessionId}] 未启用或不存在，跳过心跳", sessionId);
            return HeartbeatResult.Skipped("Pet 未启用");
        }

        // ── 2. 检查活跃时段 ──
        if (!IsWithinActiveHours(petConfig))
        {
            _logger.LogDebug("Pet [{SessionId}] 不在活跃时段，跳过心跳", sessionId);
            return HeartbeatResult.Skipped("不在活跃时段");
        }

        // ── 3. 正在处理消息时跳过（Dispatching 由 MicroPet 管理）──
        if (petState.BehaviorState == PetBehaviorState.Dispatching)
        {
            _logger.LogDebug("Pet [{SessionId}] 正在处理消息（Dispatching），跳过心跳", sessionId);
            return HeartbeatResult.Skipped("正在 Dispatching");
        }

        try
        {
            // ── 4. 构建自我感知报告 ──
            int ragChunkCount = 0;
            try
            {
                ragChunkCount = await _petRagScope.GetChunkCountAsync(sessionId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pet [{SessionId}] RAG 分块数查询失败", sessionId);
            }

            var report = await _reportBuilder.BuildAsync(
                sessionId,
                recentMessageSummaries: null, // 心跳时不传消息摘要（仅消息流时提供）
                petRagChunkCount: ragChunkCount,
                ct);

            if (report is null)
            {
                _logger.LogWarning("Pet [{SessionId}] 无法构建自我感知报告", sessionId);
                return HeartbeatResult.Failed("无法构建自我感知报告");
            }

            // ── 5. 调用 PetStateMachine（LLM 驱动状态决策）──
            var decision = await _stateMachine.EvaluateAsync(report, ct);

            _logger.LogInformation(
                "Pet [{SessionId}] 心跳决策: newState={NewState}, actions={ActionCount}, reason={Reason}",
                sessionId, decision.NewState, decision.PlannedActions.Count, decision.Reason);

            // 通知前端状态变更
            await _petNotifier.NotifyStateChangedAsync(sessionId, decision.NewState.ToString(), decision.Reason, ct);

            // ── 6. 执行 PlannedActions ──
            int actionsSucceeded = 0;
            int actionsFailed = 0;

            if (decision.PlannedActions is { Count: > 0 })
            {
                var results = await _actionExecutor.ExecuteAsync(sessionId, decision.PlannedActions, ct);
                actionsSucceeded = results.Count(r => r.Succeeded);
                actionsFailed = results.Count(r => !r.Succeeded);

                if (actionsFailed > 0)
                {
                    var failures = results.Where(r => !r.Succeeded)
                        .Select(r => $"{r.ActionType}: {r.Error}");
                    _logger.LogWarning("Pet [{SessionId}] {FailCount} 个动作执行失败: {Failures}",
                        sessionId, actionsFailed, string.Join("; ", failures));
                }
            }

            // ── 7. 更新 LastHeartbeatAt ──
            var currentState = await _stateStore.LoadAsync(sessionId, ct);
            if (currentState is not null)
            {
                var updated = currentState with
                {
                    LastHeartbeatAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                await _stateStore.SaveAsync(updated, ct);
            }

            return HeartbeatResult.Success(decision.NewState, actionsSucceeded, actionsFailed);
        }
        catch (OperationCanceledException)
        {
            return HeartbeatResult.Failed("操作已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pet [{SessionId}] 心跳执行异常", sessionId);
            return HeartbeatResult.Failed(ex.Message);
        }
    }

    /// <summary>检查当前 UTC 时间是否在配置的活跃时段内。</summary>
    internal static bool IsWithinActiveHours(PetConfig config)
    {
        // 未配置活跃时段时始终活跃
        if (config.ActiveHoursStart is null || config.ActiveHoursEnd is null)
            return true;

        int start = config.ActiveHoursStart.Value;
        int end = config.ActiveHoursEnd.Value;
        int currentHour = DateTimeOffset.UtcNow.Hour;

        if (start <= end)
        {
            // 同一天内的时段：如 8-22
            return currentHour >= start && currentHour < end;
        }
        else
        {
            // 跨午夜时段：如 22-8
            return currentHour >= start || currentHour < end;
        }
    }
}

/// <summary>心跳执行结果。</summary>
public sealed record HeartbeatResult
{
    public bool Executed { get; init; }
    public bool IsSuccess { get; init; }
    public string? SkipReason { get; init; }
    public string? Error { get; init; }
    public PetBehaviorState? NewState { get; init; }
    public int ActionsSucceeded { get; init; }
    public int ActionsFailed { get; init; }

    public static HeartbeatResult Skipped(string reason) =>
        new() { Executed = false, IsSuccess = true, SkipReason = reason };

    public static HeartbeatResult Success(PetBehaviorState newState, int actionsSucceeded, int actionsFailed) =>
        new() { Executed = true, IsSuccess = true, NewState = newState, ActionsSucceeded = actionsSucceeded, ActionsFailed = actionsFailed };

    public static HeartbeatResult Failed(string error) =>
        new() { Executed = true, IsSuccess = false, Error = error };
}
