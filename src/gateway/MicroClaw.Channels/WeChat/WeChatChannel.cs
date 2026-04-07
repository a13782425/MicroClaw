using System.Security.Cryptography;
using System.Text;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Channels.WeChat;

public sealed class WeChatChannel : IChannel
{
    public string Name => "WeChat";

    public ChannelType Type => ChannelType.WeChat;

    public string DisplayName => "微信";

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<string?> HandleWebhookAsync(string body, ChannelEntity channelEntity, CancellationToken cancellationToken = default)
    {
        // 消息正文由端点层完成签名验证后传入；此处预留完整 XML 解析实现
        return Task.FromResult<string?>(null);
    }

    public Task<ChannelTestResult> TestConnectionAsync(ChannelEntity channelEntity, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChannelTestResult(false, "微信渠道连通性测试尚未实现", 0));
    }

    /// <summary>
    /// 验证微信公众号 Webhook 签名。
    /// 算法：SHA1(字典序拼接([token, timestamp, nonce]) 或 [token, timestamp, nonce, msgEncrypt])。
    /// </summary>
    /// <param name="token">在微信公众平台配置的 Token。</param>
    /// <param name="timestamp">请求中的 timestamp 参数。</param>
    /// <param name="nonce">请求中的 nonce 参数。</param>
    /// <param name="expectedSignature">请求中的 signature 参数（明文模式）或 msg_signature 参数（安全模式）。</param>
    /// <param name="msgEncrypt">加密消息体（安全模式下为密文，明文模式传 null）。</param>
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

        // 按字典序升序排列参与签名的字段
        string[] parts = msgEncrypt is null
            ? [token, timestamp, nonce]
            : [token, timestamp, nonce, msgEncrypt];

        Array.Sort(parts, StringComparer.Ordinal);
        string content = string.Concat(parts);

        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(content));
        string computed = Convert.ToHexStringLower(hash);

        // 使用固定时间比较防止时序攻击
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(expectedSignature));
    }

    /// <summary>检查时间戳是否在容差范围内（防重放）。</summary>
    public static bool IsTimestampFresh(string? timestamp, int toleranceSeconds)
    {
        if (!long.TryParse(timestamp, out long unixSeconds))
            return false;

        DateTimeOffset requestTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        double diff = Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalSeconds);
        return diff <= toleranceSeconds;
    }
}
