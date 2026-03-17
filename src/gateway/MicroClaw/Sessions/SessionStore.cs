using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Gateway.Contracts.Sessions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MicroClaw.Sessions;

/// <summary>
/// 线程安全的会话存储，元数据持久化到 sessions.yaml，
/// 每个会话的消息历史存储在 {id}/messages.json。
/// </summary>
public sealed class SessionStore
{
    private readonly string _sessionsDir;
    private readonly string _metaFilePath;
    private readonly object _lock = new();
    private List<SessionEntry> _entries;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SessionStore(string sessionsDir)
    {
        _sessionsDir = sessionsDir;
        _metaFilePath = Path.Combine(sessionsDir, "sessions.yaml");
        Directory.CreateDirectory(sessionsDir);
        _entries = LoadMeta();
    }

    public IReadOnlyList<SessionInfo> All
    {
        get
        {
            lock (_lock)
            {
                return _entries.Select(ToInfo).ToList().AsReadOnly();
            }
        }
    }

    public SessionInfo? Get(string id)
    {
        lock (_lock)
        {
            SessionEntry? entry = _entries.FirstOrDefault(e => e.Id == id);
            return entry is null ? null : ToInfo(entry);
        }
    }

    public SessionInfo Create(string title, string providerId)
    {
        SessionEntry entry = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            ProviderId = providerId,
            IsApproved = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            _entries.Add(entry);
            SaveMeta(_entries);
        }

        Directory.CreateDirectory(GetSessionDir(entry.Id));
        return ToInfo(entry);
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            bool removed = _entries.RemoveAll(e => e.Id == id) > 0;
            if (removed)
            {
                SaveMeta(_entries);
                string dir = GetSessionDir(id);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            return removed;
        }
    }

    public SessionInfo? Approve(string id)
    {
        lock (_lock)
        {
            int index = _entries.FindIndex(e => e.Id == id);
            if (index < 0) return null;

            _entries[index] = _entries[index] with { IsApproved = true };
            SaveMeta(_entries);
            return ToInfo(_entries[index]);
        }
    }

    public void AddMessage(string sessionId, SessionMessage message)
    {
        string dir = GetSessionDir(sessionId);
        Directory.CreateDirectory(dir);

        string filePath = Path.Combine(dir, "messages.json");

        lock (_lock)
        {
            List<MessageJson> messages = LoadMessages(filePath);
            messages.Add(MessageJson.From(message));
            File.WriteAllText(filePath, JsonSerializer.Serialize(messages, JsonOptions));
        }
    }

    public IReadOnlyList<SessionMessage> GetMessages(string sessionId)
    {
        string filePath = Path.Combine(GetSessionDir(sessionId), "messages.json");
        lock (_lock)
        {
            return LoadMessages(filePath)
                .Select(m => m.ToRecord())
                .ToList()
                .AsReadOnly();
        }
    }

    private string GetSessionDir(string id) => Path.Combine(_sessionsDir, id);

    private static List<MessageJson> LoadMessages(string filePath)
    {
        if (!File.Exists(filePath)) return [];
        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<MessageJson>>(json, JsonOptions) ?? [];
    }

    private List<SessionEntry> LoadMeta()
    {
        if (!File.Exists(_metaFilePath)) return [];

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        using StreamReader reader = new(_metaFilePath);
        SessionsYamlRoot? root = deserializer.Deserialize<SessionsYamlRoot>(reader);
        if (root?.Sessions is null) return [];

        return root.Sessions
            .Select(e => new SessionEntry
            {
                Id = e.Id ?? Guid.NewGuid().ToString("N"),
                Title = e.Title ?? string.Empty,
                ProviderId = e.ProviderId ?? string.Empty,
                IsApproved = e.IsApproved,
                CreatedAt = e.CreatedAt
            })
            .ToList();
    }

    private void SaveMeta(List<SessionEntry> entries)
    {
        SessionsYamlRoot root = new()
        {
            Sessions = entries.Select(e => new SessionEntryYaml
            {
                Id = e.Id,
                Title = e.Title,
                ProviderId = e.ProviderId,
                IsApproved = e.IsApproved,
                CreatedAt = e.CreatedAt
            }).ToList()
        };

        ISerializer serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        Directory.CreateDirectory(_sessionsDir);
        File.WriteAllText(_metaFilePath, serializer.Serialize(root));
    }

    private static SessionInfo ToInfo(SessionEntry e) =>
        new(e.Id, e.Title, e.ProviderId, e.IsApproved, e.CreatedAt);
}

internal sealed record SessionEntry
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public bool IsApproved { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class SessionsYamlRoot
{
    public List<SessionEntryYaml> Sessions { get; set; } = [];
}

internal sealed class SessionEntryYaml
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? ProviderId { get; set; }
    public bool IsApproved { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
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
        Role,
        Content,
        ThinkContent,
        Timestamp,
        Attachments?.Select(a => new MessageAttachment(a.FileName, a.MimeType, a.Base64Data))
                   .ToList()
                   .AsReadOnly());
}

internal sealed class AttachmentJson
{
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Base64Data { get; set; } = string.Empty;
}
