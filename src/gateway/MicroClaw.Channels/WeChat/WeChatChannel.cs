using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using System.Security.Cryptography;
using System.Text;

namespace MicroClaw.Channels.WeChat;

public sealed class WeChatChannelProvider : IChannelProvider
{
    public string Name => "WeChat";

    public ChannelType Type => ChannelType.WeChat;

    public string DisplayName => "微信";

    public IChannel Create(ChannelEntity config) => new WeChatChannel(config);

    public Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<WebhookResult> HandleWebhookAsync(ChannelEntity config, string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
    {
        // 消息正文由端点层完成签名验证后传入；此处预留完整 XML 解析实现
        return Task.FromResult(WebhookResult.Empty);
    }

    public Task<ChannelTestResult> TestConnectionAsync(ChannelEntity config, CancellationToken cancellationToken = default)
        => Task.FromResult(new ChannelTestResult(false, "微信渠道连通性测试尚未实现", 0));
}

public sealed class WeChatChannel(ChannelEntity config) : IChannel
{
    public string Id => Config.Id;

    public string Name => "WeChat";

    public ChannelType Type => ChannelType.WeChat;

    public ChannelEntity Config { get; } = config;

    public string DisplayName => string.IsNullOrWhiteSpace(Config.DisplayName) ? "微信" : Config.DisplayName;

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<WebhookResult> HandleWebhookAsync(string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
    {
        WeChatChannelSettings settings = WeChatChannelSettings.TryParse(Config.SettingJson) ?? new();
        if (string.IsNullOrWhiteSpace(settings.Token))
            return Task.FromResult(WebhookResult.Unauthorized("Token is not configured."));

        headers ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        headers.TryGetValue("timestamp",     out string? timestamp);
        headers.TryGetValue("nonce",         out string? nonce);
        headers.TryGetValue("msg_signature", out string? msgSignature);
        headers.TryGetValue("signature",     out string? signature);
        headers.TryGetValue("echostr",       out string? echostr);

        // GET URL 验证：微信服务器发送 echostr 校验回调地址（明文模式，3 字段签名）
        if (!string.IsNullOrEmpty(echostr))
        {
            if (!IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
                return Task.FromResult(WebhookResult.Unauthorized("Timestamp expired or invalid"));
            if (!VerifySignature(settings.Token, timestamp, nonce, signature))
                return Task.FromResult(WebhookResult.Unauthorized("Signature verification failed"));
            return Task.FromResult(WebhookResult.OkText(echostr));
        }

        // POST 消息接收：验证时间戳
        if (!IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            return Task.FromResult(WebhookResult.Unauthorized("Timestamp expired or invalid"));

        headers.TryGetValue("encrypt", out string? msgEncrypt);

        if (!string.IsNullOrEmpty(msgSignature))
        {
            // 加密模式：使用 4 字段签名（含 Encrypt）
            string? encryptParam = string.IsNullOrEmpty(msgEncrypt) ? null : msgEncrypt;
            if (!VerifySignature(settings.Token, timestamp, nonce, msgSignature, encryptParam))
                return Task.FromResult(WebhookResult.Unauthorized("Signature verification failed"));
        }
        else
        {
            // 明文模式：使用 3 字段签名
            if (!VerifySignature(settings.Token, timestamp, nonce, signature))
                return Task.FromResult(WebhookResult.Unauthorized("Signature verification failed"));
        }

        // 消息正文处理（预留完整 XML 解析实现）
        return Task.FromResult(WebhookResult.Empty);
    }

    public Task<ChannelDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ChannelDiagnostics.Ok(Config.Id, "wechat"));

    public Task<string?> HandleSessionMessageAsync(SessionMessage message, SessionMessageContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<ChannelTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new ChannelTestResult(false, "微信渠道连通性测试尚未实现", 0));

    /// <summary>
    /// 验证微信公众号 Webhook 签名。
    /// 算法：SHA1(字典序拼接([token, timestamp, nonce]) 或 [token, timestamp, nonce, msgEncrypt])。
    /// </summary>
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
