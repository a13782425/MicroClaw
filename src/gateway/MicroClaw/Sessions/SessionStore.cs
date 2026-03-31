using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Channels;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Sessions;

/// <summary>
/// 会话元数据存储在 SQLite，消息历史存储在 {sessionsDir}/{id}/messages.jsonl（JSON Lines 格式）。
/// 旧版 messages.json 在首次读写时自动迁移并重命名为 messages.json.bak。
/// </summary>
public sealed class SessionStore(IDbContextFactory<GatewayDbContext> factory, string sessionsDir) : ISessionReader, ISessionMessageRemover
{
    // 新格式（JSON Lines，追加写入）
    private const string JsonlFileName = "messages.jsonl";

    // 读取旧 JSON 数组时使用（兼容迁移）
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // 写入 JSONL 单行时使用（紧凑、不换行）
    private static readonly JsonSerializerOptions JsonLinesOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // 每个 sessionId 对应一把写锁，防止并发追加写造成文件损坏
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new();

    public IReadOnlyList<SessionInfo> All
    {
        get
        {
            using GatewayDbContext db = factory.CreateDbContext();
            return db.Sessions
                .OrderByDescending(e => e.CreatedAtMs)
                .Select(e => ToInfo(e))
                .ToList()
                .AsReadOnly();
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
            using GatewayDbContext db = factory.CreateDbContext();
            return db.Sessions
                .Where(e => e.ParentSessionId == null)
                .OrderByDescending(e => e.CreatedAtMs)
                .Select(e => ToInfo(e))
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// 沿 ParentSessionId 链向上遍历，返回根会话 ID（顶层无 parent 的会话）。
    /// 支持任意嵌套深度（如 A→B→C，从 C 出发返回 A）。
    /// </summary>
    public string GetRootSessionId(string sessionId)
    {
        string current = sessionId;
        // 最多遍历 20 层，防止数据损坏导致死循环
        for (int i = 0; i < 20; i++)
        {
            using GatewayDbContext db = factory.CreateDbContext();
            SessionEntity? entity = db.Sessions.Find(current);
            if (entity?.ParentSessionId is null)
                return current;
            current = entity.ParentSessionId;
        }
        return current;
    }

    /// <summary>
    /// 在同一父会话 + 同一 AgentId 下查找空闲子代理会话（未在 activeSessionIds 中）。
    /// 返回最近创建的一个，用于复用，避免重复创建子代理会话。
    /// </summary>
    public SessionInfo? FindIdleSubAgentSession(
        string parentSessionId, string agentId, IReadOnlyCollection<string> activeSessionIds)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        return db.Sessions
            .Where(e => e.ParentSessionId == parentSessionId && e.AgentId == agentId)
            .Where(e => !activeSessionIds.Contains(e.Id))
            .OrderByDescending(e => e.CreatedAtMs)
            .Select(e => ToInfo(e))
            .FirstOrDefault();
    }

    public SessionInfo? Get(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SessionEntity? entity = db.Sessions.Find(id);
        return entity is null ? null : ToInfo(entity);
    }

    public SessionInfo Create(string title, string providerId, ChannelType channelType = ChannelType.Web, string? id = null, string? agentId = null, string? parentSessionId = null, string? channelId = null)
    {
        SessionEntity entity = new()
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

        using GatewayDbContext db = factory.CreateDbContext();
        db.Sessions.Add(entity);
        db.SaveChanges();

        Directory.CreateDirectory(GetSessionDir(entity.Id));
        return ToInfo(entity);
    }

    public bool Delete(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SessionEntity? entity = db.Sessions.Find(id);
        if (entity is null) return false;
        db.Sessions.Remove(entity);
        db.SaveChanges();

        string dir = GetSessionDir(id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        return true;
    }

    public SessionInfo? Approve(string id, string? reason = null)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SessionEntity? entity = db.Sessions.Find(id);
        if (entity is null) return null;
        entity.IsApproved = true;
        entity.ApprovalReason = reason;
        db.SaveChanges();
        return ToInfo(entity);
    }

    public SessionInfo? Disable(string id, string? reason = null)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SessionEntity? entity = db.Sessions.Find(id);
        if (entity is null) return null;
        entity.IsApproved = false;
        entity.ApprovalReason = reason;
        db.SaveChanges();
        return ToInfo(entity);
    }

    /// <summary>更新会话绑定的 Provider（用于中途切换模型）。</summary>
    public SessionInfo? UpdateProvider(string id, string providerId)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SessionEntity? entity = db.Sessions.Find(id);
        if (entity is null) return null;
        entity.ProviderId = providerId;
        db.SaveChanges();
        return ToInfo(entity);
    }

    public void AddMessage(string sessionId, SessionMessage message)    {
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

    private static List<MessageJson> LoadMessages(string filePath)
    {
        if (!File.Exists(filePath)) return [];
        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<MessageJson>>(json, JsonOptions) ?? [];
    }

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
