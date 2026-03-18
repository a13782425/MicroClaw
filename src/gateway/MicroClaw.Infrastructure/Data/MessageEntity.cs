namespace MicroClaw.Infrastructure.Data;

public sealed class MessageEntity
{
    public int Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ThinkContent { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string? AttachmentsJson { get; set; }

    public SessionEntity Session { get; set; } = null!;
}
