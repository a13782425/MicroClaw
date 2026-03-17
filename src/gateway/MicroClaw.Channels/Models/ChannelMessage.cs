namespace MicroClaw.Channels.Models;

public sealed record ChannelMessage(string UserId, string Content, DateTimeOffset UtcNow);
