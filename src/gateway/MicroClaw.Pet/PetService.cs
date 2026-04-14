using System.Runtime.CompilerServices;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Agent;
using AgentEntity = MicroClaw.Agent.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 服务层：负责 Session/Pet 查找、惰性加载和消息路由。
/// <para>
/// 实现 <see cref="IAgentMessageHandler"/> 供渠道消息处理器（飞书/企微/重试任务）路由调用。
/// 核心消息处理逻辑委托给 <see cref="MicroPet.HandleChatAsync"/>。
/// </para>
/// </summary>
public sealed class PetService : IPetService, IAgentMessageHandler, IService
{
    private readonly ISessionService _sessionRepo;
    private readonly PetContextFactory _petContextFactory;
    private readonly AgentStore _agentStore;
    private readonly ILogger<PetService> _logger;

    public PetService(IServiceProvider sp)
    {
        _sessionRepo = sp.GetRequiredService<ISessionService>();
        _petContextFactory = sp.GetRequiredService<PetContextFactory>();
        _agentStore = sp.GetRequiredService<AgentStore>();
        _logger = sp.GetRequiredService<ILogger<PetService>>();
    }

    // ── IService ─────────────────────────────────────────────────────────
    public int InitOrder => 25;
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── IPetService ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IPet?> GetOrLoadPetAsync(IMicroSession session, CancellationToken ct = default)
    {
        IPet? pet = session.Pet;
        if (pet is not null)
            return pet;

        if (!session.IsApproved)
            return null;

        return await _petContextFactory.LoadAsync(session, ct);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamItem> HandleMessageAsync(
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default,
        string source = "chat",
        string? channelId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // ── 1. 找 Session ──
        IMicroSession? session = _sessionRepo.Get(sessionId);
        if (session is null)
            throw new InvalidOperationException($"Session '{sessionId}' not found.");

        // ── 2. 找 Pet（惰性加载） ──
        IPet? pet = await GetOrLoadPetAsync(session, ct);
        if (pet is null)
            throw new InvalidOperationException($"Pet not initialized for session '{sessionId}'.");

        // ── 3. 委托 Pet 处理（渠道已保存消息 & 加载历史，使用 HandleMessageAsync）──
        await foreach (var item in pet.HandleMessageAsync(history, ct, source))
            yield return item;
    }

    // ── IAgentMessageHandler ────────────────────────────────────────────────

    /// <summary>检查是否有启用的默认 Agent（渠道消息路由前置检查）。</summary>
    public bool HasAgentForChannel(string channelId)
    {
        AgentEntity? main = _agentStore.GetDefaultAgent();
        return main is { IsEnabled: true };
    }

    /// <summary>
    /// 渠道消息入口：查找 Session → 查找 Pet → 委托 Pet.HandleMessageAsync()。
    /// </summary>
    public IAsyncEnumerable<StreamItem> HandleMessageAsync(
        string channelId,
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        CancellationToken ct = default) =>
        HandleMessageAsync(sessionId, history, ct, source: "channel", channelId: channelId);
}
