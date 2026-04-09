using System.Security.Cryptography;
using System.Text;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Channels.WeCom;

public sealed class WeComChannelProvider : IChannelProvider
{
    public string Name => "WeCom";

    public ChannelType Type => ChannelType.WeCom;

    public string DisplayName => "企业微信";

    public IChannel Create(ChannelEntity config) => new WeComChannel(config);

    public Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<string?> HandleWebhookAsync(ChannelEntity config, string body, CancellationToken cancellationToken = default)
    {
        // 消息正文由端点层完成签名验证后传入；此处预留完整 XML 解析实现
        return Task.FromResult<string?>(null);
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

    public Task<string?> HandleWebhookAsync(string body, CancellationToken cancellationToken = default)
    {
        // 消息正文由端点层完成签名验证后传入；此处预留完整 XML 解析实现
        return Task.FromResult<string?>(null);
    }

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
    {
        if (!long.TryParse(timestamp, out long unixSeconds))
            return false;

        DateTimeOffset requestTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        double diff = Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalSeconds);
        return diff <= toleranceSeconds;
    }
}
