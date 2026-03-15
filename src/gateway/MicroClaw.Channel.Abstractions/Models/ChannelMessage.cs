namespace MicroClaw.Channel.Abstractions.Models;

public sealed record ChannelMessage(string UserId, string Content, DateTimeOffset UtcNow);