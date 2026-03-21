using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Gateway.Contracts;

namespace MicroClaw.Channels;

public sealed record FeishuChannelSettings
{
    [JsonPropertyName("appId")]
    public string AppId { get; init; } = string.Empty;

    [JsonPropertyName("appSecret")]
    public string AppSecret { get; init; } = string.Empty;

    [JsonPropertyName("encryptKey")]
    public string EncryptKey { get; init; } = string.Empty;

    [JsonPropertyName("verificationToken")]
    public string VerificationToken { get; init; } = string.Empty;

    /// <summary>连接模式："websocket"（长连接，默认）或 "webhook"（回调）。</summary>
    [JsonPropertyName("connectionMode")]
    public string ConnectionMode { get; init; } = "websocket";

    /// <summary>Webhook 时间戳防重放容差（秒），默认 300（5 分钟）。</summary>
    [JsonPropertyName("webhookTimestampToleranceSeconds")]
    public int WebhookTimestampToleranceSeconds { get; init; } = 300;

    public static FeishuChannelSettings? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<FeishuChannelSettings>(json); }
        catch { return null; }
    }
}

/// <summary>企业微信渠道配置。</summary>
public sealed record WeComChannelSettings
{
    /// <summary>企业 ID（CorpId）</summary>
    [JsonPropertyName("corpId")]
    public string CorpId { get; init; } = string.Empty;

    /// <summary>应用 AgentId</summary>
    [JsonPropertyName("agentId")]
    public string AgentId { get; init; } = string.Empty;

    /// <summary>应用 Secret</summary>
    [JsonPropertyName("corpSecret")]
    public string CorpSecret { get; init; } = string.Empty;

    /// <summary>消息接收服务器 Token（用于签名验证）</summary>
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    /// <summary>消息加解密密钥（43 位 Base64，留空则使用明文模式）</summary>
    [JsonPropertyName("encodingAesKey")]
    public string EncodingAesKey { get; init; } = string.Empty;

    /// <summary>Webhook 时间戳防重放容差（秒），默认 300（5 分钟）。</summary>
    [JsonPropertyName("webhookTimestampToleranceSeconds")]
    public int WebhookTimestampToleranceSeconds { get; init; } = 300;

    public static WeComChannelSettings? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<WeComChannelSettings>(json); }
        catch { return null; }
    }
}

/// <summary>微信公众号渠道配置。</summary>
public sealed record WeChatChannelSettings
{
    /// <summary>公众号 AppId</summary>
    [JsonPropertyName("appId")]
    public string AppId { get; init; } = string.Empty;

    /// <summary>公众号 AppSecret</summary>
    [JsonPropertyName("appSecret")]
    public string AppSecret { get; init; } = string.Empty;

    /// <summary>消息接收服务器 Token（用于签名验证）</summary>
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    /// <summary>消息加解密密钥（43 位 Base64，留空则使用明文模式）</summary>
    [JsonPropertyName("encodingAesKey")]
    public string EncodingAesKey { get; init; } = string.Empty;

    /// <summary>Webhook 时间戳防重放容差（秒），默认 300（5 分钟）。</summary>
    [JsonPropertyName("webhookTimestampToleranceSeconds")]
    public int WebhookTimestampToleranceSeconds { get; init; } = 300;

    public static WeChatChannelSettings? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<WeChatChannelSettings>(json); }
        catch { return null; }
    }
}

public sealed record ChannelConfig
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public ChannelType ChannelType { get; init; }
    public string ProviderId { get; init; } = string.Empty;
    public bool IsEnabled { get; init; } = true;
    public string SettingsJson { get; init; } = "{}";
}

/// <summary>渠道连通性测试结果。</summary>
public sealed record ChannelTestResult(bool Success, string Message, long LatencyMs);
