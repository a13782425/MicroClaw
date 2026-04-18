using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration;
using MicroClaw.Core;
using MicroClaw.Utils;

namespace MicroClaw.Sessions;

/// <summary>
/// 单会话消息持久化组件。承载指定 <see cref="MicroSession"/> 的 messages.jsonl 读写，
/// 以组件内单独的 <see cref="SemaphoreSlim"/> 作为 per-session 写入门禁。
/// </summary>
public sealed class SessionMessagesComponent : MicroComponent
{
    private const string JsonlFileName = "messages.jsonl";

    private static readonly JsonSerializerOptions JsonLinesOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>宿主会话的 Id。</summary>
    public string SessionId => GetRequiredSession().Id;

    private MicroSession GetRequiredSession()
    {
        object host = GetRequiredHost();
        return host is MicroSession session
            ? session
            : throw new InvalidOperationException($"{nameof(SessionMessagesComponent)} can only be attached to a {nameof(MicroSession)}.");
    }

    private string GetSessionDir() => Path.Combine(MicroClawConfig.Env.SessionsDir, SessionId);

    protected override ValueTask OnInitializedAsync(CancellationToken cancellationToken = default)
    {
        MicroClawUtils.CheckDirectory(GetSessionDir());
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposedAsync(CancellationToken cancellationToken = default)
    {
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>向 messages.jsonl 追加一条消息。</summary>
    public void AddMessage(SessionMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        string dir = GetSessionDir();
        Directory.CreateDirectory(dir);
        string jsonlPath = Path.Combine(dir, JsonlFileName);

        _writeLock.Wait();
        try
        {
            string line = JsonSerializer.Serialize(MessageJson.From(message), JsonLinesOptions);
            File.AppendAllText(jsonlPath, line + "\n");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>返回当前会话的全部消息。</summary>
    public IReadOnlyList<SessionMessage> GetMessages() => ReadAllMessages();

    /// <summary>分页读取会话消息（按时间倒序 skip + limit）。</summary>
    public (IReadOnlyList<SessionMessage> Messages, int Total) GetMessagesPaged(int skip, int limit)
    {
        List<SessionMessage> all = ReadAllMessages();
        int total = all.Count;
        int endIdx = Math.Max(0, total - skip);
        int startIdx = Math.Max(0, endIdx - limit);
        return (all.Skip(startIdx).Take(endIdx - startIdx).ToList().AsReadOnly(), total);
    }

    /// <summary>按 Id 集合批量移除会话消息。</summary>
    public void RemoveMessages(IReadOnlySet<string> messageIds)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        if (messageIds.Count == 0) return;

        string jsonlPath = Path.Combine(GetSessionDir(), JsonlFileName);
        if (!File.Exists(jsonlPath)) return;

        _writeLock.Wait();
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
            _writeLock.Release();
        }
    }

    private List<SessionMessage> ReadAllMessages()
    {
        string jsonlPath = Path.Combine(GetSessionDir(), JsonlFileName);
        if (!File.Exists(jsonlPath)) return [];

        return File.ReadLines(jsonlPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<MessageJson>(line, JsonLinesOptions)!)
            .Where(m => m is not null)
            .Select(m => m.ToRecord())
            .ToList();
    }

    private sealed class MessageJson
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
                Attachments = m.Attachments?.Select(a => new AttachmentJson { FileName = a.FileName, MimeType = a.MimeType, Base64Data = a.Base64Data }).ToList(),
            };

        public SessionMessage ToRecord() => new(
            Id ?? Guid.NewGuid().ToString("N"),
            Role,
            Content,
            ThinkContent,
            Timestamp,
            Attachments?.Select(a => new MessageAttachment(a.FileName, a.MimeType, a.Base64Data)).ToList().AsReadOnly(),
            Source,
            MessageType,
            Metadata,
            Visibility);
    }

    private sealed class AttachmentJson
    {
        public string FileName { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string Base64Data { get; set; } = string.Empty;
    }
}
