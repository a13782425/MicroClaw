using System.Security.Cryptography;
using System.Text;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Channels.WeChat;

public sealed class WeChatChannelProvider : IChannelProvider
{
    public string Name => "WeChat";

    public ChannelType Type => ChannelType.WeChat;

    public string DisplayName => "微信";

    public IChannel Create(ChannelEntity config) => new WeChatChannel(config);

    public Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<string?> HandleWebhookAsync(ChannelEntity config, string body, CancellationToken cancellationToken = default)
    {
        // 消息正文由端点层完成签名验证后传入；此处预留完整 XML 解析实现
        return Task.FromResult<string?>(null);
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

    public Task<string?> HandleWebhookAsync(string body, CancellationToken cancellationToken = default)
    {
        // 消息正文由端点层完成签名验证后传入；此处预留完整 XML 解析实现
        return Task.FromResult<string?>(null);
    }

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
    {
        if (!long.TryParse(timestamp, out long unixSeconds))
            return false;

        DateTimeOffset requestTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        double diff = Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalSeconds);
        return diff <= toleranceSeconds;
    }
}
