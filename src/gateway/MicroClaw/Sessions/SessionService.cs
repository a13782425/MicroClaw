using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MicroClaw.Agent;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Channels;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Core;
using MicroClaw.Hubs;
using MicroClaw.Infrastructure;
using MicroClaw.Utils;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace MicroClaw.Sessions;
/// <summary>
/// 会话统一服务：合并了原 <c>SessionStore</c>（持久化）与 <c>ChannelSessionService</c>（渠道会话管理）的职责。
/// <para>
/// 作为 <see cref="MicroService"/> 参与 <see cref="MicroEngine"/> 的启停：<see cref="Order"/> 保持 20；
/// 在 <c>StartAsync</c> 阶段预热会话缓存并为每个 <see cref="MicroSession"/> 挂接
/// <see cref="SessionMessagesComponent"/>，由组件封装消息的 jsonl 读写。
/// </para>
/// <para>
/// 持久化：会话元数据写入 sessions.yaml；消息历史写入 {sessionsDir}/{id}/messages.jsonl（由组件负责）。
/// </para>
/// </summary>
public sealed class SessionService : MicroService, ISessionService
{
    private AgentStore? agentStore;
    private IHubContext<GatewayHub>? hubContext;
    private IPetFactory? _petFactory;
    private readonly IServiceProvider serviceProvider;
    private ConcurrentDictionary<string, MicroSession> _sessions = new();
    
    public SessionService(IServiceProvider sp)
    {
        serviceProvider = sp;
    }
    
    /// <inheritdoc />
    public override int Order => 20;
    
    private readonly ReaderWriterLockSlim _metaLock = new(LockRecursionPolicy.NoRecursion);
    
    private static readonly ConcurrentDictionary<string, DateTimeOffset> NotifyThrottle = new();
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// 服务启动：惰性解析运行时依赖、准备 sessions 目录、从 YAML 重构全部历史会话，
    /// 并为每个 <see cref="MicroSession"/> 挂接 <see cref="SessionMessagesComponent"/>。
    /// </summary>
    protected override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        agentStore ??= serviceProvider.GetRequiredService<AgentStore>();
        hubContext ??= serviceProvider.GetRequiredService<IHubContext<GatewayHub>>();
        _petFactory ??= serviceProvider.GetRequiredService<IPetFactory>();
        MicroClawUtils.CheckDirectory(MicroClawConfig.Env.SessionsDir);
        
        ConcurrentDictionary<string, MicroSession> warmedSessions = new();
        foreach (SessionEntity entity in MicroClawConfig.Get<SessionsOptions>().Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MicroSession microSession = await MicroSession.CreateAsync(entity, serviceProvider, cancellationToken);
            if (!warmedSessions.TryAdd(microSession.Id, microSession))
                throw new InvalidOperationException($"Duplicate session id '{microSession.Id}' found while warming cache.");
        }
        
        _sessions = warmedSessions;
    }
    
    /// <summary>
    /// 服务停止：释放全部已挂接的 <see cref="MicroSession"/> 并清空缓存。
    /// 若在启动阶段补偿调用（<see cref="MicroService.ActivationFailed"/> 为 true），
    /// 按同样策略尽力清理已部分挂接的 sessions。
    /// </summary>
    protected override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ConcurrentDictionary<string, MicroSession> snapshot = Interlocked.Exchange(ref _sessions, new());
        
        List<Exception> errors = [];
        foreach (MicroSession session in snapshot.Values)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }
        
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1)
            throw new AggregateException(errors);
    }
    
    /// <inheritdoc/>
    public async Task<SessionInfo> FindOrCreateSession(ChannelType channelType, string channelId, string senderId, string channelDisplayName, string providerId)
    {
        string sessionId = GenerateSessionId(channelType, channelId, senderId);
        if (_sessions.TryGetValue(sessionId, out MicroSession? existing))
            return existing.ToInfo();
        
        string senderShort = senderId.Length > 8 ? senderId[..8] : senderId;
        string title = $"{channelDisplayName}-{senderShort}";
        SessionEntity entity = new()
        {
            Id = sessionId,
            Title = title,
            ProviderId = providerId,
            ChannelType = ChannelService.SerializeChannelType(channelType),
            ChannelId = channelId,
            CreatedAtMs = TimeUtils.NowMs(),
            AgentId = agentStore!.GetDefault()?.Id,
        };
        MicroSession microSession = await MicroSession.CreateAsync(entity, serviceProvider);
        AddToCacheAndPersist(microSession);
        
        SessionInfo created = microSession.ToInfo();
        
        _ = hubContext!.Clients.All.SendAsync("sessionCreated", new { sessionId = created.Id, title = created.Title, channelType = ChannelService.SerializeChannelType(channelType) });
        
        return created;
    }
    
    /// <inheritdoc/>
    public async Task NotifyPendingApprovalAsync(string sessionId, string sessionTitle, ChannelType channelType)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (NotifyThrottle.TryGetValue(sessionId, out DateTimeOffset lastNotify) && now - lastNotify < ThrottleInterval)
            return;
        
        NotifyThrottle[sessionId] = now;
        
        await hubContext!.Clients.All.SendAsync("sessionPendingApproval", new { sessionId, sessionTitle, channelType = ChannelService.SerializeChannelType(channelType), timestamp = now });
    }
    
    /// <inheritdoc/>
    public async Task<bool> CheckApprovalAsync(SessionInfo session, ChannelType channelType)
    {
        if (session.IsApproved) return true;
        await NotifyPendingApprovalAsync(session.Id, session.Title, channelType);
        return false;
    }
    
    /// <inheritdoc/>
    public async Task<IMicroSession> CreateSession(string title, string providerId, ChannelType channelType = ChannelType.Web, string? id = null, string? agentId = null, string? channelId = null)
    {
        SessionEntity entity = new()
        {
            Id = id ?? MicroClawUtils.GetUniqueId(),
            Title = title,
            ProviderId = providerId,
            ChannelType = ChannelService.SerializeChannelType(channelType),
            ChannelId = channelId ?? ChannelService.WebChannelId,
            CreatedAtMs = TimeUtils.NowMs(),
            AgentId = agentId,
        };
        
        MicroSession microSession = await MicroSession.CreateAsync(entity, serviceProvider);
        AddToCacheAndPersist(microSession);
        return microSession;
    }
    
    
    public IMicroSession? Get(string id) => _sessions.TryGetValue(id, out MicroSession? microSession) ? microSession : null;
    
    public IReadOnlyList<IMicroSession> GetAll() => _sessions.Values.OrderByDescending(s => s.CreatedAt).Cast<IMicroSession>().ToList().AsReadOnly();
    
    public void Save(IMicroSession microSession)
    {
        _metaLock.EnterWriteLock();
        try
        {
            if (!_sessions.TryGetValue(microSession.Id, out MicroSession? cached) || !ReferenceEquals(cached, microSession))
            {
                throw new InvalidOperationException($"Session '{microSession.Id}' must be saved through the cached instance.");
            }
            
            MicroClawUtils.CheckDirectory(GetSessionDir(microSession.Id));
            PersistCacheSnapshot();
        }
        finally
        {
            _metaLock.ExitWriteLock();
        }
    }
    
    public bool Delete(string id)
    {
        MicroSession? removed;
        _metaLock.EnterWriteLock();
        try
        {
            if (!_sessions.TryRemove(id, out removed)) return false;
            PersistCacheSnapshot();
        }
        finally
        {
            _metaLock.ExitWriteLock();
        }
        
        // 释放 MicroSession + 其挂载的组件。组件的 Dispose 是纯内存同步路径，
        // 用 sync-over-async 等待以便保持 ISessionService.Delete 的同步签名。
        try
        {
            removed.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // 清理失败不应阻断上层删除流程；文件删除仍然需要执行。
        }
        
        string dir = GetSessionDir(id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        
        return true;
    }
    
    
    /// <inheritdoc />
    public void AddMessage(string sessionId, SessionMessage message)
    {
        SessionMessagesComponent component = ResolveMessages(sessionId);
        component.AddMessage(message);
    }
    
    /// <inheritdoc />
    public IReadOnlyList<SessionMessage> GetMessages(string sessionId) => ResolveMessages(sessionId).GetMessages();
    
    /// <inheritdoc />
    public (IReadOnlyList<SessionMessage> Messages, int Total) GetMessagesPaged(string sessionId, int skip, int limit) => ResolveMessages(sessionId).GetMessagesPaged(skip, limit);
    
    /// <inheritdoc />
    public void RemoveMessages(string sessionId, IReadOnlySet<string> messageIds) => ResolveMessages(sessionId).RemoveMessages(messageIds);
    
    
    private SessionMessagesComponent ResolveMessages(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out MicroSession? session))
            throw new InvalidOperationException($"Session '{sessionId}' is not loaded.");
        
        return session.Messages;
    }
    
    private string GetSessionDir(string id) => Path.Combine(MicroClawConfig.Env.SessionsDir, id);
    
    private void AddToCacheAndPersist(MicroSession microSession)
    {
        _metaLock.EnterWriteLock();
        try
        {
            if (!_sessions.TryAdd(microSession.Id, microSession))
                throw new InvalidOperationException($"Session '{microSession.Id}' already exists in cache.");
            
            try
            {
                MicroClawUtils.CheckDirectory(GetSessionDir(microSession.Id));
                PersistCacheSnapshot();
            }
            catch
            {
                _sessions.TryRemove(microSession.Id, out _);
                throw;
            }
        }
        finally
        {
            _metaLock.ExitWriteLock();
        }
    }
    
    private void PersistCacheSnapshot()
    {
        List<SessionEntity> newItems = _sessions.Values.OrderBy(s => s.CreatedAt).Select(s => s.Entity.DeepClone()).ToList();
        
        MicroClawConfig.Save(new SessionsOptions { Items = newItems });
    }
    
    /// <summary>生成确定性会话 ID：SHA256(channelType:channelId:senderId) 的前 32 个十六进制字符。</summary>
    internal static string GenerateSessionId(ChannelType channelType, string channelId, string senderId)
    {
        string input = $"{channelType}:{channelId}:{senderId}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash)[..32];
    }
}