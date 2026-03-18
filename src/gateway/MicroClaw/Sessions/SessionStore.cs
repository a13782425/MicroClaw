using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Channels;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Sessions;

/// <summary>
/// 会话元数据存储在 SQLite，消息历史存储在 {sessionsDir}/{id}/messages.json。
/// </summary>
public sealed class SessionStore(IDbContextFactory<GatewayDbContext> factory, string sessionsDir)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<SessionInfo> All
    {
        get
        {
            using GatewayDbContext db = factory.CreateDbContext();
            return db.Sessions
                .OrderByDescending(e => e.CreatedAtUtc)
                .Select(e => ToInfo(e))
                .ToList()
                .AsReadOnly();
        }
    }

    public SessionInfo? Get(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SessionEntity? entity = db.Sessions.Find(id);
        return entity is null ? null : ToInfo(entity);
    }

    public SessionInfo Create(string title, string providerId, ChannelType channelType = ChannelType.Web, string? id = null)
    {
        SessionEntity entity = new()
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Title = title,
            ProviderId = providerId,
            IsApproved = false,
            ChannelType = ChannelConfigStore.SerializeChannelType(channelType),
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O")
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

    public SessionInfo? Approve(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SessionEntity? entity = db.Sessions.Find(id);
        if (entity is null) return null;
        entity.IsApproved = true;
        db.SaveChanges();
        return ToInfo(entity);
    }

    public SessionInfo? Disable(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        SessionEntity? entity = db.Sessions.Find(id);
        if (entity is null) return null;
        entity.IsApproved = false;
        db.SaveChanges();
        return ToInfo(entity);
    }

    public void AddMessage(string sessionId, SessionMessage message)
    {
        string dir = GetSessionDir(sessionId);
        Directory.CreateDirectory(dir);
        string filePath = Path.Combine(dir, "messages.json");

        List<MessageJson> messages = LoadMessages(filePath);
        messages.Add(MessageJson.From(message));
        File.WriteAllText(filePath, JsonSerializer.Serialize(messages, JsonOptions));
    }

    public IReadOnlyList<SessionMessage> GetMessages(string sessionId)
    {
        string filePath = Path.Combine(GetSessionDir(sessionId), "messages.json");
        return LoadMessages(filePath)
            .Select(m => m.ToRecord())
            .ToList()
            .AsReadOnly();
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
            DateTimeOffset.TryParse(e.CreatedAtUtc, out DateTimeOffset dt) ? dt : DateTimeOffset.MinValue);
}

internal sealed class MessageJson
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ThinkContent { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public List<AttachmentJson>? Attachments { get; set; }

    public static MessageJson From(SessionMessage m) => new()
    {
        Role = m.Role,
        Content = m.Content,
        ThinkContent = m.ThinkContent,
        Timestamp = m.Timestamp,
        Attachments = m.Attachments?.Select(a => new AttachmentJson
        {
            FileName = a.FileName,
            MimeType = a.MimeType,
            Base64Data = a.Base64Data
        }).ToList()
    };

    public SessionMessage ToRecord() => new(
        Role, Content, ThinkContent, Timestamp,
        Attachments?.Select(a => new MessageAttachment(a.FileName, a.MimeType, a.Base64Data))
                   .ToList().AsReadOnly());
}

internal sealed class AttachmentJson
{
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Base64Data { get; set; } = string.Empty;
}
