using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MicroClaw.Channels.Models;
using MicroClaw.Gateway.Contracts;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

public sealed class FeishuChannel(
    FeishuMessageProcessor processor,
    ILogger<FeishuChannel> logger) : IChannel
{
    public string Name => "Feishu";

    public ChannelType Type => ChannelType.Feishu;

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task<string?> HandleWebhookAsync(string body, ChannelConfig channelConfig, CancellationToken ct = default)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(channelConfig.SettingsJson) ?? new();
        logger.LogDebug("飞书 Webhook 收到请求 channel={ChannelId}", channelConfig.Id);

        // 尝试解密（如果启用了 EncryptKey）
        string decrypted = TryDecrypt(body, settings.EncryptKey);

        // URL Verification
        FeishuUrlVerificationRequest? verification = TryDeserialize<FeishuUrlVerificationRequest>(decrypted);
        if (verification?.Type == "url_verification")
        {
            logger.LogInformation("飞书 URL 验证 channel={ChannelId}", channelConfig.Id);
            return JsonSerializer.Serialize(new { challenge = verification.Challenge });
        }

        // Event Callback v2
        FeishuEventCallback<FeishuMessageEvent>? callback = TryDeserialize<FeishuEventCallback<FeishuMessageEvent>>(decrypted);
        if (callback?.Header?.EventType == "im.message.receive_v1" && callback.Event is not null)
        {
            string? userText = FeishuMessageProcessor.ExtractText(callback.Event);
            if (!string.IsNullOrWhiteSpace(userText)
                && !string.IsNullOrWhiteSpace(callback.Event.Message?.MessageId)
                && !string.IsNullOrWhiteSpace(callback.Event.Message?.ChatId))
            {
                string? senderId = callback.Event.Sender?.SenderId?.OpenId;
                string chatId = callback.Event.Message.ChatId;
                string messageId = callback.Event.Message.MessageId;

                // 异步处理消息，不阻塞飞书回调
                _ = Task.Run(() => processor.ProcessMessageAsync(
                    userText, senderId, chatId, messageId, channelConfig, settings, ct: CancellationToken.None));
            }
        }

        return JsonSerializer.Serialize(new { code = 0, msg = "ok" });
    }

    /// <summary>验证飞书 Webhook 签名：SHA256(timestamp + nonce + encryptKey + body) == expectedSignature。</summary>
    public static bool VerifyWebhookSignature(string? timestamp, string? nonce, string encryptKey, string body, string? expectedSignature)
    {
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(expectedSignature))
            return false;

        string content = timestamp + nonce + encryptKey + body;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        string computed = Convert.ToHexStringLower(hash);

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

    private static T? TryDeserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }

    private static string TryDecrypt(string body, string encryptKey)
    {
        if (string.IsNullOrWhiteSpace(encryptKey)) return body;

        try
        {
            JsonNode? node = JsonNode.Parse(body);
            string? encrypted = node?["encrypt"]?.GetValue<string>();
            if (encrypted is null) return body;

            return AesDecrypt(encryptKey, encrypted);
        }
        catch
        {
            return body;
        }
    }

    private static string AesDecrypt(string key, string encrypted)
    {
        byte[] keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        byte[] data = Convert.FromBase64String(encrypted);

        byte[] iv = data[..16];
        byte[] cipher = data[16..];

        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        byte[] decrypted = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(decrypted);
    }
}
