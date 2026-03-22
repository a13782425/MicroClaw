namespace MicroClaw.Gateway.Contracts.Sessions;

public sealed record SessionInfo(
    string Id,
    string Title,
    string ProviderId,
    bool IsApproved,
    ChannelType ChannelType,
    DateTimeOffset CreatedAt,
    string? AgentId = null,
    string? ParentSessionId = null,
    string? ApprovalReason = null);

public sealed record SessionMessage(
    string Role,
    string Content,
    string? ThinkContent,
    DateTimeOffset Timestamp,
    IReadOnlyList<MessageAttachment>? Attachments,
    string? Source = null);

public sealed record MessageAttachment(
    string FileName,
    string MimeType,
    string Base64Data);

public sealed record CreateSessionRequest(
    string Title,
    string ProviderId);

public sealed record DeleteSessionRequest(string Id);

public sealed record ApproveSessionRequest(string Id, string? Reason = null);

public sealed record DisableSessionRequest(string Id, string? Reason = null);

public sealed record BatchApproveSessionRequest(IReadOnlyList<string> Ids, string? Reason = null);

public sealed record BatchDisableSessionRequest(IReadOnlyList<string> Ids, string? Reason = null);

public sealed record ChatRequest(
    string Content,
    IReadOnlyList<MessageAttachment>? Attachments);

public sealed record SwitchProviderRequest(
    string Id,
    string ProviderId);
