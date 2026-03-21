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

public sealed record ChannelConfig
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public ChannelType ChannelType { get; init; }
    public string ProviderId { get; init; } = string.Empty;
    public bool IsEnabled { get; init; } = true;
    public string SettingsJson { get; init; } = "{}";
}
