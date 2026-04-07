using System.Text.Json;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Sessions;

public sealed record SessionInfo(
    string Id,
    string Title,
    string ProviderId,
    bool IsApproved,
    ChannelType ChannelType,
    string ChannelId,
    DateTimeOffset CreatedAt,
    string? AgentId = null,
    string? ParentSessionId = null,
    string? ApprovalReason = null);

public sealed record SessionMessage(
    string Id,
    string Role,
    string Content,
    string? ThinkContent,
    DateTimeOffset Timestamp,
    IReadOnlyList<MessageAttachment>? Attachments,
    string? Source = null,
    string? MessageType = null,
    IReadOnlyDictionary<string, JsonElement>? Metadata = null,
    string? Visibility = null);

public sealed record MessageAttachment(
    string FileName,
    string MimeType,
    string Base64Data);

public sealed record CreateSessionRequest(
    string Title,
    string ProviderId,
    string? ChannelId = null,
    string? AgentId = null);

public sealed record DeleteSessionRequest(string Id);

public sealed record ApproveSessionRequest(string Id, string? Reason = null);

public sealed record DisableSessionRequest(string Id, string? Reason = null);

public sealed record ChatRequest(
    string Content,
    IReadOnlyList<MessageAttachment>? Attachments);

public sealed record SwitchProviderRequest(
    string Id,
    string ProviderId);
