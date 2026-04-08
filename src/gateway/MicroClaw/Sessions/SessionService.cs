using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Agent;
using MicroClaw.Channels;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Hubs;
using MicroClaw.Infrastructure;
using MicroClaw.Utils;
using Microsoft.AspNetCore.SignalR;

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
public sealed class SessionService(
    AgentStore agentStore,
    IHubContext<GatewayHub> hubContext,
    IEnumerable<IChannel> channels,
    IPetFactory petFactory,
    string sessionsDir) : ISessionService
{
    // ── 持久化常量 ──────────────────────────────────────────────────────────
    private const string JsonlFileName = "messages.jsonl";

    private static readonly JsonSerializerOptions JsonLinesOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── 并发控制 ────────────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new();
    private readonly ReaderWriterLockSlim _metaLock = new(LockRecursionPolicy.NoRecursion);

    // ── 待批准通知限流（5 分钟/session）────────────────────────────────────
    private static readonly ConcurrentDictionary<string, DateTimeOffset> NotifyThrottle = new();
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(5);

    private readonly IReadOnlyDictionary<ChannelType, IChannel> _channels = channels
        .GroupBy(static channel => channel.Type)
        .ToDictionary(static group => group.Key, static group => group.Last());
    private readonly IPetFactory _petFactory = petFactory ?? throw new ArgumentNullException(nameof(petFactory));

    // ── ISessionService: 渠道会话管理 ──────────────────────────────────────

    /// <inheritdoc/>
    public SessionInfo FindOrCreateSession(ChannelType channelType, string channelId, string senderId,
        string channelDisplayName, string providerId)
    {
        string sessionId = GenerateSessionId(channelType, channelId, senderId);

        IMicroSession? existing = ((ISessionRepository)this).Get(sessionId);
        if (existing is not null) return existing.ToInfo();

        string senderShort = senderId.Length > 8 ? senderId[..8] : senderId;
        string title = $"{channelDisplayName}-{senderShort}";

        MicroSession microSession = MicroSession.Create(
            id: sessionId,
            title: title,
            providerId: providerId,
            channelType: channelType,
            channelId: channelId,
            createdAt: TimeUtils.NowOffset(),
            agentId: agentStore.GetDefault()?.Id);
        AttachRuntimeDependencies(microSession);

        ((ISessionRepository)this).Save(microSession);

        SessionInfo created = microSession.ToInfo();

        _ = hubContext.Clients.All.SendAsync("sessionCreated", new
        {
            sessionId = created.Id,
            title = created.Title,
            channelType = ChannelConfigStore.SerializeChannelType(channelType)
        });

        return created;
    }

    /// <inheritdoc/>
    public async Task NotifyPendingApprovalAsync(string sessionId, string sessionTitle, ChannelType channelType)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (NotifyThrottle.TryGetValue(sessionId, out DateTimeOffset lastNotify)
            && now - lastNotify < ThrottleInterval)
            return;

        NotifyThrottle[sessionId] = now;

        await hubContext.Clients.All.SendAsync("sessionPendingApproval", new
        {
            sessionId,
            sessionTitle,
            channelType = ChannelConfigStore.SerializeChannelType(channelType),
            timestamp = now
        });
    }

    /// <inheritdoc/>
    public async Task<bool> CheckApprovalAsync(SessionInfo session, ChannelType channelType)
    {
        if (session.IsApproved) return true;
        await NotifyPendingApprovalAsync(session.Id, session.Title, channelType);
        return false;
    }

    /// <inheritdoc/>
    public MicroSession CreateSession(string title, string providerId,
        ChannelType channelType = ChannelType.Web,
        string? id = null,
        string? agentId = null,
        string? channelId = null)
    {
        MicroSession microSession = MicroSession.Create(
            id: id ?? MicroClawUtils.GetUniqueId(),
            title: title,
            providerId: providerId,
            channelType: channelType,
            channelId: channelId ?? ChannelConfigStore.WebChannelId,
            createdAt: TimeUtils.NowOffset(),
            agentId: agentId);
        AttachRuntimeDependencies(microSession);
        ((ISessionRepository)this).Save(microSession);
        return microSession;
    }

    IMicroSession ISessionService.CreateSession(string title, string providerId,
        ChannelType channelType,
        string? id,
        string? agentId,
        string? channelId)
        => CreateSession(title, providerId, channelType, id, agentId, channelId);

    // ── ISessionRepository: 查询 ───────────────────────────────────────────

    IMicroSession? ISessionRepository.Get(string id)
    {
        SessionEntity? entity;
        _metaLock.EnterReadLock();
        try
        {
            entity = GetItems().FirstOrDefault(e => e.Id == id);
        }
        finally { _metaLock.ExitReadLock(); }

        if (entity is null) return null;
        MicroSession microSession = ReconstitueFromEntity(entity);
        AttachRuntimeDependencies(microSession);
        return microSession;
    }

    IReadOnlyList<IMicroSession> ISessionRepository.GetAll()
    {
        List<SessionEntity> entities;
        _metaLock.EnterReadLock();
        try
        {
            entities = GetItems()
                .OrderByDescending(e => e.CreatedAtMs)
                .ToList();
        }
        finally { _metaLock.ExitReadLock(); }

        List<MicroSession> sessions = entities.Select(ReconstitueFromEntity).ToList();
        foreach (MicroSession session in sessions)
            AttachRuntimeDependencies(session);
        return sessions.Cast<IMicroSession>().ToList().AsReadOnly();
    }

    // ── ISessionRepository: 命令 ───────────────────────────────────────────

    void ISessionRepository.Save(IMicroSession microSession)
    {
        MicroSession mutableMicroSession = RequireMutable(microSession);
        _metaLock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<SessionsOptions>();
            int idx = opts.Items.FindIndex(e => e.Id == mutableMicroSession.Id);
            List<SessionEntity> newItems;
            if (idx >= 0)
            {
                long originalCreatedAtMs = opts.Items[idx].CreatedAtMs;
                SessionEntity entity = ToEntity(mutableMicroSession) with { CreatedAtMs = originalCreatedAtMs };
                newItems = new List<SessionEntity>(opts.Items) { [idx] = entity };
            }
            else
            {
                SessionEntity entity = ToEntity(mutableMicroSession) with { CreatedAtMs = TimeUtils.NowMs() };
                Directory.CreateDirectory(GetSessionDir(entity.Id));
                newItems = [.. opts.Items, entity];
            }
            MicroClawConfig.Save(new SessionsOptions { Items = newItems });
        }
        finally { _metaLock.ExitWriteLock(); }
    }

    bool ISessionRepository.Delete(string id)
    {
        _metaLock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<SessionsOptions>();
            if (!opts.Items.Any(e => e.Id == id)) return false;
            MicroClawConfig.Save(new SessionsOptions { Items = opts.Items.Where(e => e.Id != id).ToList() });
        }
        finally { _metaLock.ExitWriteLock(); }

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
        finally { sem.Release(); }
    }

    IReadOnlyList<SessionMessage> ISessionRepository.GetMessages(string sessionId)
        => ReadAllMessages(sessionId);

    (IReadOnlyList<SessionMessage> Messages, int Total) ISessionRepository.GetMessagesPaged(
        string sessionId, int skip, int limit)
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
            string[] kept = File.ReadLines(jsonlPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line =>
                {
                    MessageJson? m = JsonSerializer.Deserialize<MessageJson>(line, JsonLinesOptions);
                    return m is null || m.Id is null || !messageIds.Contains(m.Id);
                })
                .ToArray();
            File.WriteAllLines(jsonlPath, kept);
        }
        finally { sem.Release(); }
    }

    // ── 私有辅助 ────────────────────────────────────────────────────────────

    private string GetSessionDir(string id) => Path.Combine(sessionsDir, id);

    private static List<SessionEntity> GetItems()
        => MicroClawConfig.Get<SessionsOptions>().Items;

    private List<SessionMessage> ReadAllMessages(string sessionId)
    {
        string jsonlPath = Path.Combine(GetSessionDir(sessionId), JsonlFileName);
        if (!File.Exists(jsonlPath)) return [];

        return File.ReadLines(jsonlPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<MessageJson>(line, JsonLinesOptions)!)
            .Where(m => m is not null)
            .Select(m => m.ToRecord())
            .ToList();
    }

    private MicroSession ReconstitueFromEntity(SessionEntity e) =>
        MicroSession.Reconstitute(
            id: e.Id,
            title: e.Title,
            providerId: e.ProviderId,
            isApproved: e.IsApproved,
            channelType: ChannelConfigStore.ParseChannelType(e.ChannelType),
            channelId: string.IsNullOrEmpty(e.ChannelId) ? ChannelConfigStore.WebChannelId : e.ChannelId,
            createdAt: TimeUtils.FromMs(e.CreatedAtMs),
            agentId: e.AgentId,
            approvalReason: e.ApprovalReason);

    private void AttachRuntimeDependencies(MicroSession microSession)
    {
        ArgumentNullException.ThrowIfNull(microSession);

        microSession.AttachChannel(ResolveChannel(microSession.ChannelType));

        IPet? pet = _petFactory.CreateOrLoadAsync(microSession).GetAwaiter().GetResult();
        if (pet is not null)
            microSession.AttachPet(pet);
    }

    private IChannel ResolveChannel(ChannelType channelType)
    {
        if (_channels.TryGetValue(channelType, out IChannel? channel))
            return channel;

        if (_channels.TryGetValue(ChannelType.Web, out IChannel? fallback))
            return fallback;

        throw new InvalidOperationException($"No channel is registered for type '{channelType}'.");
    }

    private static SessionEntity ToEntity(IMicroSession s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        ProviderId = s.ProviderId,
        IsApproved = s.IsApproved,
        ChannelType = ChannelConfigStore.SerializeChannelType(s.ChannelType),
        ChannelId = s.ChannelId,
        CreatedAtMs = 0,
        AgentId = s.AgentId,
        ApprovalReason = s.ApprovalReason,
    };

    private static MicroSession RequireMutable(IMicroSession microSession) =>
        microSession as MicroSession
        ?? throw new InvalidOperationException("Session write model must be the concrete MicroClaw.Sessions.Session type.");

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

    public static MessageJson From(SessionMessage m) => new()
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
        Attachments = m.Attachments?.Select(a => new AttachmentJson
        {
            FileName = a.FileName,
            MimeType = a.MimeType,
            Base64Data = a.Base64Data
        }).ToList()
    };

    public SessionMessage ToRecord() => new(
        Id ?? Guid.NewGuid().ToString("N"),
        Role, Content, ThinkContent, Timestamp,
        Attachments?.Select(a => new MessageAttachment(a.FileName, a.MimeType, a.Base64Data))
                   .ToList().AsReadOnly(),
        Source,
        MessageType,
        Metadata,
        Visibility);
}

internal sealed class AttachmentJson
{
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Base64Data { get; set; } = string.Empty;
}
