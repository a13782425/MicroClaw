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

    /// <summary>F-B-1: 消息中被 @ 的人员列表。</summary>
    [JsonPropertyName("mentions")]
    public FeishuMention[]? Mentions { get; init; }
}

/// <summary>F-B-1: 飞书消息 @mention 条目。</summary>
public sealed record FeishuMention
{
    /// <summary>mention 的占位 key，如 "@_user_1"。</summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>被 @ 用户的 ID 集合。</summary>
    [JsonPropertyName("id")]
    public FeishuMentionId? Id { get; init; }

    /// <summary>被 @ 用户的显示名称。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>F-B-1: 被 @ 用户的 ID 集合。</summary>
public sealed record FeishuMentionId
{
    [JsonPropertyName("open_id")]
    public string? OpenId { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("union_id")]
    public string? UnionId { get; init; }
}

/// <summary>文本消息的 content JSON 内层。</summary>
public sealed record FeishuTextContent
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

/// <summary>图片消息的 content JSON 内层（message_type = "image"）。</summary>
public sealed record FeishuImageContent
{
    [JsonPropertyName("image_key")]
    public string? ImageKey { get; init; }
}

/// <summary>文件消息的 content JSON 内层（message_type = "file"）。</summary>
public sealed record FeishuFileContent
{
    [JsonPropertyName("file_key")]
    public string? FileKey { get; init; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }

    [JsonPropertyName("file_type")]
    public string? FileType { get; init; }
}

/// <summary>富文本消息的 content JSON 外层（message_type = "post"），按语言分组。</summary>
public sealed record FeishuPostContent
{
    [JsonPropertyName("zh_cn")]
    public FeishuPostBody? ZhCn { get; init; }

    [JsonPropertyName("en_us")]
    public FeishuPostBody? EnUs { get; init; }
}

/// <summary>富文本消息某一语言版本的正文（含标题 + 段落列表）。</summary>
public sealed record FeishuPostBody
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>段落列表，每个段落是一组行内元素。</summary>
    [JsonPropertyName("content")]
    public FeishuPostElement[][]? Content { get; init; }
}

/// <summary>富文本行内元素（text / a / at 等标签）。</summary>
public sealed record FeishuPostElement
{
    [JsonPropertyName("tag")]
    public string? Tag { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>链接元素（tag = "a"）的目标 URL。</summary>
    [JsonPropertyName("href")]
    public string? Href { get; init; }

    /// <summary>@提及元素（tag = "at"）的用户 OpenId。</summary>
    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    /// <summary>@提及元素（tag = "at"）的用户显示名称。</summary>
    [JsonPropertyName("user_name")]
    public string? UserName { get; init; }
}
