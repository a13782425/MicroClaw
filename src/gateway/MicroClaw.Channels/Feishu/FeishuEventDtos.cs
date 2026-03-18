using System.Text.Json.Serialization;

namespace MicroClaw.Channels.Feishu;

/// <summary>飞书 URL 验证请求（首次配置回调 URL 时的握手）。</summary>
public sealed record FeishuUrlVerificationRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("challenge")]
    public string? Challenge { get; init; }

    [JsonPropertyName("token")]
    public string? Token { get; init; }
}

/// <summary>飞书事件回调 V2 包装。</summary>
public sealed record FeishuEventCallback<TEvent>
{
    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    [JsonPropertyName("header")]
    public FeishuEventHeader? Header { get; init; }

    [JsonPropertyName("event")]
    public TEvent? Event { get; init; }
}

/// <summary>飞书事件回调头部。</summary>
public sealed record FeishuEventHeader
{
    [JsonPropertyName("event_id")]
    public string? EventId { get; init; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; init; }

    [JsonPropertyName("create_time")]
    public string? CreateTime { get; init; }

    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("app_id")]
    public string? AppId { get; init; }

    [JsonPropertyName("tenant_key")]
    public string? TenantKey { get; init; }
}

/// <summary>im.message.receive_v1 事件体。</summary>
public sealed record FeishuMessageEvent
{
    [JsonPropertyName("sender")]
    public FeishuMessageSender? Sender { get; init; }

    [JsonPropertyName("message")]
    public FeishuMessageBody? Message { get; init; }
}

/// <summary>消息发送者。</summary>
public sealed record FeishuMessageSender
{
    [JsonPropertyName("sender_id")]
    public FeishuSenderId? SenderId { get; init; }

    [JsonPropertyName("sender_type")]
    public string? SenderType { get; init; }
}

/// <summary>发送者 ID 集合。</summary>
public sealed record FeishuSenderId
{
    [JsonPropertyName("open_id")]
    public string? OpenId { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("union_id")]
    public string? UnionId { get; init; }
}

/// <summary>消息体。</summary>
public sealed record FeishuMessageBody
{
    [JsonPropertyName("message_id")]
    public string? MessageId { get; init; }

    [JsonPropertyName("root_id")]
    public string? RootId { get; init; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; init; }

    [JsonPropertyName("chat_id")]
    public string? ChatId { get; init; }

    [JsonPropertyName("chat_type")]
    public string? ChatType { get; init; }

    [JsonPropertyName("message_type")]
    public string? MessageType { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

/// <summary>文本消息的 content JSON 内层。</summary>
public sealed record FeishuTextContent
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
