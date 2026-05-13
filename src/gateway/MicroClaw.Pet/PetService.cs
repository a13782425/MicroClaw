using System.Runtime.CompilerServices;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;
/// <summary>
/// Pet 服务层：负责 Session/Pet 查找、惰性加载和消息路由。
/// <para>
/// 核心消息处理逻辑委托给 <see cref="MicroPet.HandleChatAsync"/>。
/// </para>
/// </summary>
public sealed class PetService : IService
{
    private readonly ISessionService _sessionRepo;
    private readonly PetContextFactory _petContextFactory;
    private readonly IMicroAgentService _agentService;
    private readonly ILogger<PetService> _logger;
    
    public PetService(IServiceProvider sp)
    {
        _sessionRepo = sp.GetRequiredService<ISessionService>();
        _petContextFactory = sp.GetRequiredService<PetContextFactory>();
        _agentService = sp.GetRequiredService<IMicroAgentService>();
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
}