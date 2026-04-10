using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Agent;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Channels;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Hubs;
using MicroClaw.Infrastructure;
using MicroClaw.Utils;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace MicroClaw.Sessions;
/// <summary>
/// 会话统一服务：合并了原 <c>SessionStore</c>（持久化）与 <c>ChannelSessionService</c>（渠道会话管理）的职责。
/// <para>
/// 实现 <see cref="ISessionService"/> 和 <see cref="ISessionRepository"/>，
/// 后者供 AgentRunner 等内部组件继续使用，无需迁移接口名称。
/// </para>
/// <para>
/// 持久化：会话元数据写入 sessions.yaml；消息历史写入 {sessionsDir}/{id}/messages.jsonl。
/// </para>
/// </summary>
public sealed class SessionService : ISessionService
{
    // ── 持久化常量 ──────────────────────────────────────────────────────────
    private const string JsonlFileName = "messages.jsonl";
    
    private static readonly JsonSerializerOptions JsonLinesOptions = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    
    // ── 依赖字段 ────────────────────────────────────────────────────────────
    private AgentStore? agentStore;
    private IHubContext<GatewayHub>? hubContext;
    private IPetFactory? _petFactory;
    private readonly IServiceProvider serviceProvider;
    private ConcurrentDictionary<string, MicroSession> _sessions = new();
    
    public SessionService(IServiceProvider sp)
    {
        serviceProvider = sp;
    }
    
    // ── IService ────────────────────────────────────────────────────────────
    public int InitOrder => 20;
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        agentStore ??= serviceProvider.GetRequiredService<AgentStore>();
        hubContext ??= serviceProvider.GetRequiredService<IHubContext<GatewayHub>>();
        _petFactory ??= serviceProvider.GetRequiredService<IPetFactory>();
        MicroClawUtils.CheckDirectory(MicroClawConfig.Env.SessionsDir);
        
        ConcurrentDictionary<string, MicroSession> warmedSessions = new();
        foreach (SessionEntity entity in MicroClawConfig.Get<SessionsOptions>().Items)
        {
            ct.ThrowIfCancellationRequested();
            
            MicroSession microSession = MicroSession.Reconstitute(entity);
            await InitializeRuntimeDependenciesAsync(microSession, ct);
            if (!warmedSessions.TryAdd(microSession.Id, microSession))
                throw new InvalidOperationException($"Duplicate session id '{microSession.Id}' found while warming cache.");
        }
        
        _sessions = warmedSessions;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    
    // ── 并发控制 ────────────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new();
    private readonly ReaderWriterLockSlim _metaLock = new(LockRecursionPolicy.NoRecursion);
    
    // ── 待批准通知限流（5 分钟/session）────────────────────────────────────
    private static readonly ConcurrentDictionary<string, DateTimeOffset> NotifyThrottle = new();
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(5);
    
    // ── ISessionService: 渠道会话管理 ──────────────────────────────────────
    
    /// <inheritdoc/>
    public async Task<SessionInfo> FindOrCreateSession(ChannelType channelType, string channelId, string senderId, string channelDisplayName, string providerId)
    {
        string sessionId = GenerateSessionId(channelType, channelId, senderId);
        if (_sessions.TryGetValue(sessionId, out MicroSession? existing))
            return existing.ToInfo();
        
        string senderShort = senderId.Length > 8 ? senderId[..8] : senderId;
        string title = $"{channelDisplayName}-{senderShort}";
        
        MicroSession microSession = MicroSession.Create(id: sessionId, title: title, providerId: providerId, channelType: channelType, channelId: channelId, createdAt: TimeUtils.NowOffset(), agentId: agentStore!.GetDefault()?.Id);
        await InitializeRuntimeDependenciesAsync(microSession);
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
        MicroSession microSession = MicroSession.Create(id: id ?? MicroClawUtils.GetUniqueId(), title: title, providerId: providerId, channelType: channelType, channelId: channelId ?? ChannelService.WebChannelId, createdAt: TimeUtils.NowOffset(), agentId: agentId);
        await InitializeRuntimeDependenciesAsync(microSession);
        AddToCacheAndPersist(microSession);
        return microSession;
    }
    
    // ── ISessionRepository: 查询 ───────────────────────────────────────────
    
    IMicroSession? ISessionRepository.Get(string id) => _sessions.TryGetValue(id, out MicroSession? microSession) ? microSession : null;
    
    IReadOnlyList<IMicroSession> ISessionRepository.GetAll() => _sessions.Values.OrderByDescending(s => s.CreatedAt).Cast<IMicroSession>().ToList().AsReadOnly();
    
    // ── ISessionRepository: 命令 ───────────────────────────────────────────
    
    void ISessionRepository.Save(IMicroSession microSession)
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
    
    bool ISessionRepository.Delete(string id)
    {
        _metaLock.EnterWriteLock();
        try
        {
            if (!_sessions.TryRemove(id, out _)) return false;
            PersistCacheSnapshot();
        }
        finally
        {
            _metaLock.ExitWriteLock();
        }
        
        string dir = GetSessionDir(id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        
        return true;
    }
    
    // ── ISessionRepository: 消息操作 ──────────────────────────────────────
    
    void ISessionRepository.AddMessage(string sessionId, SessionMessage message)
    {
        string dir = GetSessionDir(sessionId);
        Directory.CreateDirectory(dir);
        string jsonlPath = Path.Combine(dir, JsonlFileName);
        
        SemaphoreSlim sem = _writeLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        sem.Wait();
        try
        {
            string line = JsonSerializer.Serialize(MessageJson.From(message), JsonLinesOptions);
            File.AppendAllText(jsonlPath, line + "\n");
        }
        finally
        {
            sem.Release();
        }
    }
    
    IReadOnlyList<SessionMessage> ISessionRepository.GetMessages(string sessionId) => ReadAllMessages(sessionId);
    
    (IReadOnlyList<SessionMessage> Messages, int Total) ISessionRepository.GetMessagesPaged(string sessionId, int skip, int limit)
    {
        List<SessionMessage> all = ReadAllMessages(sessionId);
        int total = all.Count;
        int endIdx = Math.Max(0, total - skip);
        int startIdx = Math.Max(0, endIdx - limit);
        return (all.Skip(startIdx).Take(endIdx - startIdx).ToList().AsReadOnly(), total);
    }
    
    void ISessionRepository.RemoveMessages(string sessionId, IReadOnlySet<string> messageIds)
    {
        if (messageIds.Count == 0) return;
        
        string dir = GetSessionDir(sessionId);
        string jsonlPath = Path.Combine(dir, JsonlFileName);
        if (!File.Exists(jsonlPath)) return;
        
        SemaphoreSlim sem = _writeLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        sem.Wait();
        try
        {
            string[] kept = File.ReadLines(jsonlPath).Where(line => !string.IsNullOrWhiteSpace(line)).Where(line =>
            {
                MessageJson? m = JsonSerializer.Deserialize<MessageJson>(line, JsonLinesOptions);
                return m is null || m.Id is null || !messageIds.Contains(m.Id);
            }).ToArray();
            File.WriteAllLines(jsonlPath, kept);
        }
        finally
        {
            sem.Release();
        }
    }
    
    // ── 私有辅助 ────────────────────────────────────────────────────────────
    
    private string GetSessionDir(string id) => Path.Combine(MicroClawConfig.Env.SessionsDir, id);
    
    private List<SessionMessage> ReadAllMessages(string sessionId)
    {
        string jsonlPath = Path.Combine(GetSessionDir(sessionId), JsonlFileName);
        if (!File.Exists(jsonlPath)) return [];
        
        return File.ReadLines(jsonlPath).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => JsonSerializer.Deserialize<MessageJson>(line, JsonLinesOptions)!).Where(m => m is not null).Select(m => m.ToRecord()).ToList();
    }
    
    private Task InitializeRuntimeDependenciesAsync(MicroSession microSession, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(microSession);
        return AttachPetAsync(microSession, ct);
    }
    
    private async Task AttachPetAsync(MicroSession microSession, CancellationToken ct)
    {
        IPet? pet = await _petFactory!.CreateOrLoadAsync(microSession, ct);
        if (pet is not null)
            microSession.AttachPet(pet);
    }
    
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

// ── 内部 JSON 序列化辅助类 ────────────────────────────────────────────────
internal sealed class MessageJson
{
    public string? Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ThinkContent { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public List<AttachmentJson>? Attachments { get; set; }
    public string? Source { get; set; }
    public string? MessageType { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
    public string? Visibility { get; set; }
    
    public static MessageJson From(SessionMessage m) =>
        new()
        {
            Id = m.Id,
            Role = m.Role,
            Content = m.Content,
            ThinkContent = m.ThinkContent,
            Timestamp = m.Timestamp,
            Source = m.Source,
            MessageType = m.MessageType,
            Metadata = m.Metadata is not null ? new Dictionary<string, JsonElement>(m.Metadata) : null,
            Visibility = m.Visibility,
            Attachments = m.Attachments?.Select(a => new AttachmentJson { FileName = a.FileName, MimeType = a.MimeType, Base64Data = a.Base64Data }).ToList()
        };
    
    public SessionMessage ToRecord() => new(Id ?? Guid.NewGuid().ToString("N"), Role, Content, ThinkContent, Timestamp, Attachments?.Select(a => new MessageAttachment(a.FileName, a.MimeType, a.Base64Data)).ToList().AsReadOnly(), Source, MessageType, Metadata, Visibility);
}
internal sealed class AttachmentJson
{
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Base64Data { get; set; } = string.Empty;
}