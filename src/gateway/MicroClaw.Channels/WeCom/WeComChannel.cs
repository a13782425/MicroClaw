using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using System.Security.Cryptography;
using System.Text;

namespace MicroClaw.Channels.WeCom;

public sealed class WeComChannelProvider : IChannelProvider
{
    public string Name => "WeCom";

    public ChannelType Type => ChannelType.WeCom;

    public string DisplayName => "企业微信";

    public IChannel Create(ChannelEntity config) => new WeComChannel(config);

    public Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<WebhookResult> HandleWebhookAsync(ChannelEntity config, string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
    {
        // 消息正文由端点层完成签名验证后传入；此处预留完整 XML 解析实现
        return Task.FromResult(WebhookResult.Empty);
    }

    public Task<ChannelTestResult> TestConnectionAsync(ChannelEntity config, CancellationToken cancellationToken = default)
        => Task.FromResult(new ChannelTestResult(false, "企业微信渠道连通性测试尚未实现", 0));
}

public sealed class WeComChannel(ChannelEntity config) : IChannel
{
    public string Id => Config.Id;

    public string Name => "WeCom";

    public ChannelType Type => ChannelType.WeCom;

    public ChannelEntity Config { get; } = config;

    public string DisplayName => string.IsNullOrWhiteSpace(Config.DisplayName) ? "企业微信" : Config.DisplayName;

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<WebhookResult> HandleWebhookAsync(string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
    {
        WeComChannelSettings settings = WeComChannelSettings.TryParse(Config.SettingJson) ?? new();
        if (string.IsNullOrWhiteSpace(settings.Token))
            return Task.FromResult(WebhookResult.Unauthorized("Token is not configured."));

        headers ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        headers.TryGetValue("timestamp",      out string? timestamp);
        headers.TryGetValue("nonce",          out string? nonce);
        headers.TryGetValue("msg_signature",  out string? msgSignature);
        headers.TryGetValue("echostr",        out string? echostr);

        // GET URL 验证：企业微信服务器发送 echostr 校验回调地址
        if (!string.IsNullOrEmpty(echostr))
        {
            if (!IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
                return Task.FromResult(WebhookResult.Unauthorized("Timestamp expired or invalid"));
            if (!VerifySignature(settings.Token, timestamp, nonce, msgSignature))
                return Task.FromResult(WebhookResult.Unauthorized("Signature verification failed"));
            return Task.FromResult(WebhookResult.OkText(echostr));
        }

        // POST 消息接收：验证时间戳 + 签名（加密模式携带 Encrypt 字段）
        if (!IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            return Task.FromResult(WebhookResult.Unauthorized("Timestamp expired or invalid"));

        headers.TryGetValue("encrypt", out string? msgEncrypt);
        string? encryptParam = string.IsNullOrEmpty(msgEncrypt) ? null : msgEncrypt;
        if (!VerifySignature(settings.Token, timestamp, nonce, msgSignature, encryptParam))
            return Task.FromResult(WebhookResult.Unauthorized("Signature verification failed"));

        // 消息正文处理（预留完整 XML 解析实现）
        return Task.FromResult(WebhookResult.Empty);
    }

    public Task<ChannelDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ChannelDiagnostics.Ok(Config.Id, "wecom"));

    public Task<string?> HandleSessionMessageAsync(SessionMessage message, SessionMessageContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<ChannelTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new ChannelTestResult(false, "企业微信渠道连通性测试尚未实现", 0));

    /// <summary>
    /// 验证企业微信 Webhook 签名。
    /// 算法：SHA1(字典序拼接([token, timestamp, nonce]) 或 [token, timestamp, nonce, msgEncrypt])。
    /// </summary>
    /// <param name="token">在企业微信后台配置的 Token。</param>
    /// <param name="timestamp">请求中的 timestamp 参数。</param>
    /// <param name="nonce">请求中的 nonce 参数。</param>
    /// <param name="expectedSignature">请求中的 msg_signature 参数。</param>
    /// <param name="msgEncrypt">加密消息体（SafeMode 下为密文，明文模式传 null）。</param>
    public static bool VerifySignature(
        string token,
        string? timestamp,
        string? nonce,
        string? expectedSignature,
        string? msgEncrypt = null)
    {
        if (string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce)
            || string.IsNullOrEmpty(expectedSignature))
            return false;

        string[] parts = msgEncrypt is null
            ? [token, timestamp, nonce]
            : [token, timestamp, nonce, msgEncrypt];

        Array.Sort(parts, StringComparer.Ordinal);
        string content = string.Concat(parts);

        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(content));
        string computed = Convert.ToHexStringLower(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(expectedSignature));
    }

    public static bool IsTimestampFresh(string? timestamp, int toleranceSeconds)
        => WebhookUtils.IsTimestampFresh(timestamp, toleranceSeconds);
}
