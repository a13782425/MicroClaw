using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Channels;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Infrastructure;

namespace MicroClaw.Sessions;

/// <summary>
/// 会话元数据存储在 sessions.yaml（通过 MicroClawConfig），消息历史存储在 {sessionsDir}/{id}/messages.jsonl（JSON Lines 格式）。
/// </summary>
public sealed class SessionStore(string sessionsDir)
    : ISessionMessageRemover, ISessionRepository
{
    // 新格式（JSON Lines，追加写入）
    private const string JsonlFileName = "messages.jsonl";

    // 写入 JSONL 单行时使用（紧凑、不换行）
    private static readonly JsonSerializerOptions JsonLinesOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // 每个 sessionId 对应一把写锁，防止并发追加写造成文件损坏
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    public IReadOnlyList<SessionInfo> All
    {
        get
        {
            _lock.EnterReadLock();
            try { return GetItems().OrderByDescending(e => e.CreatedAtMs).Select(ToInfo).ToList().AsReadOnly(); }
            finally { _lock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// 仅返回顶层会话（即非子代理会话，ParentSessionId 为 null）。
    /// 用于会话管理列表，子代理会话不对用户暴露。
    /// </summary>
    public IReadOnlyList<SessionInfo> AllTopLevel
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return GetItems()
                    .Where(e => string.IsNullOrWhiteSpace(e.ParentSessionId))
                    .OrderByDescending(e => e.CreatedAtMs)
                    .Select(ToInfo)
                    .ToList().AsReadOnly();
            }
            finally { _lock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// 沿 ParentSessionId 链向上遍历，返回根会话 ID（顶层无 parent 的会话）。
    /// 支持任意嵌套深度（如 A→B→C，从 C 出发返回 A）。
    /// </summary>
    public string GetRootSessionId(string sessionId)
    {
        string current = sessionId;
        _lock.EnterReadLock();
        try
        {
            // 最多遍历 20 层，防止数据损坏导致死循环
            for (int i = 0; i < 20; i++)
            {
                var entity = GetItems().FirstOrDefault(e => e.Id == current);
                if (entity?.ParentSessionId is null)
                    return current;
                current = entity.ParentSessionId;
            }
            return current;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// 在同一父会话 + 同一 AgentId 下查找空闲子代理会话（未在 activeSessionIds 中）。
    /// 返回最近创建的一个，用于复用，避免重复创建子代理会话。
    /// </summary>
    public SessionInfo? FindIdleSubAgentSession(
        string parentSessionId, string agentId, IReadOnlyCollection<string> activeSessionIds)
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems()
                .Where(e => e.ParentSessionId == parentSessionId && e.AgentId == agentId)
                .Where(e => !activeSessionIds.Contains(e.Id))
                .OrderByDescending(e => e.CreatedAtMs)
                .Select(ToInfo)
                .FirstOrDefault();
        }
        finally { _lock.ExitReadLock(); }
    }

    public SessionInfo? Get(string id)
    {
        _lock.EnterReadLock();
        try { return GetItems().FirstOrDefault(e => e.Id == id) is { } e ? ToInfo(e) : null; }
        finally { _lock.ExitReadLock(); }
    }

    public SessionInfo Create(string title, string providerId, ChannelType channelType = ChannelType.Web, string? id = null, string? agentId = null, string? parentSessionId = null, string? channelId = null)
        => CreateSession(title, providerId, channelType, id, agentId, parentSessionId, channelId).ToInfo();

    /// <summary>创建新会话并返回领域对象（O-1-6）。持久化后不发布领域事件（由调用方决定）。</summary>
    public Session CreateSession(string title, string providerId, ChannelType channelType = ChannelType.Web, string? id = null, string? agentId = null, string? parentSessionId = null, string? channelId = null)
    {
        var entity = new SessionEntity
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Title = title,
            ProviderId = providerId,
            IsApproved = false,
            ChannelType = ChannelConfigStore.SerializeChannelType(channelType),
            ChannelId = channelId ?? ChannelConfigStore.WebChannelId,
            CreatedAtMs = TimeBase.NowMs(),
            AgentId = agentId,
            ParentSessionId = parentSessionId,
        };

        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<SessionsOptions>();
            MicroClawConfig.Save(new SessionsOptions { Items = [.. opts.Items, entity] });
        }
        finally { _lock.ExitWriteLock(); }

        Directory.CreateDirectory(GetSessionDir(entity.Id));
        return ToSession(entity);
    }

    public bool Delete(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<SessionsOptions>();
            if (!opts.Items.Any(e => e.Id == id)) return false;
            MicroClawConfig.Save(new SessionsOptions { Items = opts.Items.Where(e => e.Id != id).ToList() });
        }
        finally { _lock.ExitWriteLock(); }

        string dir = GetSessionDir(id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        return true;
    }

    public SessionInfo? Approve(string id, string? reason = null)
        => MutateItem(id, e => e with { IsApproved = true, ApprovalReason = reason });

    public SessionInfo? Disable(string id, string? reason = null)
        => MutateItem(id, e => e with { IsApproved = false, ApprovalReason = reason });

    /// <summary>更新会话绑定的 Provider（用于中途切换模型）。</summary>
    public SessionInfo? UpdateProvider(string id, string providerId)
        => MutateItem(id, e => e with { ProviderId = providerId });

    public void AddMessage(string sessionId, SessionMessage message)
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

    /// <summary>
    /// 从 messages.jsonl 中移除指定 ID 的消息（重写文件）。
    /// 用于上下文溢出时将已归档消息从活跃对话历史中删除。
    /// </summary>
    public void RemoveMessages(string sessionId, IReadOnlySet<string> messageIds)
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
        finally
        {
            sem.Release();
        }
    }

    public IReadOnlyList<SessionMessage> GetMessages(string sessionId)
    {
        return ReadAllMessages(sessionId);
    }

    /// <summary>
    /// 分页查询消息历史（从末尾计数）。
    /// <paramref name="skip"/> = 跳过最新的多少条；<paramref name="limit"/> = 最多返回多少条。
    /// 例：skip=0,limit=50 → 最新 50 条；skip=50,limit=50 → 往前 50 条。
    /// </summary>
    public (IReadOnlyList<SessionMessage> Messages, int Total) GetMessagesPaged(string sessionId, int skip, int limit)
    {
        List<SessionMessage> all = ReadAllMessages(sessionId);
        int total = all.Count;
        // 从末尾方向计算起始位置
        int endIdx = Math.Max(0, total - skip);
        int startIdx = Math.Max(0, endIdx - limit);
        IReadOnlyList<SessionMessage> slice = all.Skip(startIdx).Take(endIdx - startIdx).ToList().AsReadOnly();
        return (slice, total);
    }

    private List<SessionMessage> ReadAllMessages(string sessionId)
    {
        string dir = GetSessionDir(sessionId);
        string jsonlPath = Path.Combine(dir, JsonlFileName);

        if (!File.Exists(jsonlPath)) return [];

        return File.ReadLines(jsonlPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<MessageJson>(line, JsonLinesOptions)!)
            .Where(m => m is not null)
            .Select(m => m.ToRecord())
            .ToList();
    }

    private string GetSessionDir(string id) => Path.Combine(sessionsDir, id);

    private static List<SessionEntity> GetItems()
        => MicroClawConfig.Get<SessionsOptions>().Items;

    private SessionInfo? MutateItem(string id, Func<SessionEntity, SessionEntity> mutate)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<SessionsOptions>();
            int idx = opts.Items.FindIndex(e => e.Id == id);
            if (idx < 0) return null;

            var mutated = mutate(opts.Items[idx]);
            var newItems = new List<SessionEntity>(opts.Items) { [idx] = mutated };
            MicroClawConfig.Save(new SessionsOptions { Items = newItems });
            return ToInfo(mutated);
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <inheritdoc/>
    public IReadOnlyList<SessionInfo> GetAll() => All;

    // ── ISessionRepository 实现 ──────────────────────────────────────────────

    /// <summary>按 ID 获取 Session 领域对象。</summary>
    Session? ISessionRepository.Get(string id)
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems().FirstOrDefault(e => e.Id == id) is { } e ? ToSession(e) : null;
        }
        finally { _lock.ExitReadLock(); }
    }

    IReadOnlyList<Session> ISessionRepository.GetAll()
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems().OrderByDescending(e => e.CreatedAtMs).Select(ToSession).ToList().AsReadOnly();
        }
        finally { _lock.ExitReadLock(); }
    }

    IReadOnlyList<Session> ISessionRepository.GetTopLevel()
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems()
                .Where(e => string.IsNullOrWhiteSpace(e.ParentSessionId))
                .OrderByDescending(e => e.CreatedAtMs)
                .Select(ToSession)
                .ToList().AsReadOnly();
        }
        finally { _lock.ExitReadLock(); }
    }

    string ISessionRepository.GetRootSessionId(string sessionId)
        => GetRootSessionId(sessionId);

    Session? ISessionRepository.FindIdleSubAgentSession(
        string parentSessionId, string agentId, IReadOnlyCollection<string> activeSessionIds)
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems()
                .Where(e => e.ParentSessionId == parentSessionId && e.AgentId == agentId)
                .Where(e => !activeSessionIds.Contains(e.Id))
                .OrderByDescending(e => e.CreatedAtMs)
                .Select(ToSession)
                .FirstOrDefault();
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// 将 Session 领域对象的当前状态持久化（新增时 CreatedAtMs 为 0 则写入当前时间）。
    /// 若已存在则全量覆盖。
    /// </summary>
    void ISessionRepository.Save(Session session)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<SessionsOptions>();
            int idx = opts.Items.FindIndex(e => e.Id == session.Id);
            List<SessionEntity> newItems;
            if (idx >= 0)
            {
                // 更新：保留原有 CreatedAtMs
                long originalCreatedAtMs = opts.Items[idx].CreatedAtMs;
                var entity = ToEntity(session) with { CreatedAtMs = originalCreatedAtMs };
                newItems = new List<SessionEntity>(opts.Items) { [idx] = entity };
            }
            else
            {
                // 新增：写入当前时间
                var entity = ToEntity(session) with { CreatedAtMs = TimeBase.NowMs() };
                Directory.CreateDirectory(GetSessionDir(entity.Id));
                newItems = [.. opts.Items, entity];
            }
            MicroClawConfig.Save(new SessionsOptions { Items = newItems });
        }
        finally { _lock.ExitWriteLock(); }
    }

    bool ISessionRepository.Delete(string id) => Delete(id);

    void ISessionRepository.AddMessage(string sessionId, SessionMessage message)
        => AddMessage(sessionId, message);

    IReadOnlyList<SessionMessage> ISessionRepository.GetMessages(string sessionId)
        => GetMessages(sessionId);

    (IReadOnlyList<SessionMessage> Messages, int Total) ISessionRepository.GetMessagesPaged(
        string sessionId, int skip, int limit)
        => GetMessagesPaged(sessionId, skip, limit);

    void ISessionRepository.RemoveMessages(string sessionId, IReadOnlySet<string> messageIds)
        => RemoveMessages(sessionId, messageIds);

    // ── 私有辅助：SessionInfo/Session 互转 ──────────────────────────────────

    private static Session ToSession(SessionEntity e) =>
        Session.Reconstitute(
            id: e.Id,
            title: e.Title,
            providerId: e.ProviderId,
            isApproved: e.IsApproved,
            channelType: ChannelConfigStore.ParseChannelType(e.ChannelType),
            channelId: string.IsNullOrEmpty(e.ChannelId) ? ChannelConfigStore.WebChannelId : e.ChannelId,
            createdAt: TimeBase.FromMs(e.CreatedAtMs),
            agentId: e.AgentId,
            parentSessionId: e.ParentSessionId,
            approvalReason: e.ApprovalReason);

    private static SessionEntity ToEntity(Session s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        ProviderId = s.ProviderId,
        IsApproved = s.IsApproved,
        ChannelType = ChannelConfigStore.SerializeChannelType(s.ChannelType),
        ChannelId = s.ChannelId,
        CreatedAtMs = 0,  // 新增时由调用方设置；覆盖时需由外部从原始 entity 补填
        AgentId = s.AgentId,
        ParentSessionId = s.ParentSessionId,
        ApprovalReason = s.ApprovalReason,
    };

    private static SessionInfo ToInfo(SessionEntity e) =>
        new(e.Id, e.Title, e.ProviderId, e.IsApproved,
            ChannelConfigStore.ParseChannelType(e.ChannelType),
            string.IsNullOrEmpty(e.ChannelId) ? ChannelConfigStore.WebChannelId : e.ChannelId,
            TimeBase.FromMs(e.CreatedAtMs),
            e.AgentId,
            e.ParentSessionId,
            e.ApprovalReason);
}

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
