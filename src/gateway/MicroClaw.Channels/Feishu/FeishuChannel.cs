using System.Diagnostics;
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
    ChannelConfigStore configStore,
    ILogger<FeishuChannel> logger) : IChannel
{
    public string Name => "Feishu";

    public ChannelType Type => ChannelType.Feishu;

    /// <summary>
    /// F-A-1: 主动发送消息到飞书用户或群聊。
    /// <para><see cref="ChannelMessage.UserId"/> 填写目标的 open_id（ou_ 前缀）或 chat_id（oc_ 前缀）。</para>
    /// <para>使用数据库中第一个已启用的飞书渠道配置的凭据发送消息。</para>
    /// </summary>
    public async Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        ChannelConfig? channelConfig = configStore
            .GetByType(ChannelType.Feishu)
            .FirstOrDefault(c => c.IsEnabled);

        if (channelConfig is null)
        {
            logger.LogWarning("F-A-1 PublishAsync 跳过：未找到已启用的飞书渠道配置");
            return;
        }

        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(channelConfig.SettingsJson) ?? new();
        await processor.SendMessageAsync(message.UserId, message.Content, settings, cancellationToken);
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

                // F-F-1: 全链路追踪 — Webhook 接收步骤
                string traceId = messageId.Length >= 8 ? messageId[..8] : messageId;
                logger.LogInformation(
                    "[{TraceId}] Webhook 接收 channel={ChannelId} from={SenderId} messageId={MessageId}",
                    traceId, channelConfig.Id, senderId, messageId);

                // F-B-1: 提取 chatType 和被 @ 的 open_id 列表
                string chatType = callback.Event.Message.ChatType ?? "p2p";
                IReadOnlyList<string> mentionedOpenIds = callback.Event.Message.Mentions is { Length: > 0 }
                    ? callback.Event.Message.Mentions
                        .Select(m => m.Id?.OpenId)
                        .OfType<string>()
                        .ToList()
                    : [];

                // 异步处理消息，不阻塞飞书回调
                _ = Task.Run(() => processor.ProcessMessageAsync(
                    userText, senderId, chatId, messageId, channelConfig, settings,
                    chatType, mentionedOpenIds, ct: CancellationToken.None));
            }
        }

        return JsonSerializer.Serialize(new { code = 0, msg = "ok" });
    }

    /// <summary>
    /// 通过飞书 OpenAPI 获取租户访问令牌来验证 AppId + AppSecret 凭据的有效性。
    /// </summary>
    public async Task<ChannelTestResult> TestConnectionAsync(ChannelConfig config, CancellationToken cancellationToken = default)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(config.SettingsJson) ?? new();

        if (string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
            return new ChannelTestResult(false, "未配置 AppId 或 AppSecret", 0);

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            string payload = JsonSerializer.Serialize(new { app_id = settings.AppId, app_secret = settings.AppSecret });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await client.PostAsync(
                $"{settings.ApiBaseUrl.TrimEnd('/')}/open-apis/auth/v3/tenant_access_token/internal",
                content, cancellationToken);
            sw.Stop();

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            int code = doc.RootElement.GetProperty("code").GetInt32();

            if (code == 0)
                return new ChannelTestResult(true, "连接成功", sw.ElapsedMilliseconds);

            doc.RootElement.TryGetProperty("msg", out JsonElement msgEl);
            return new ChannelTestResult(false, $"飞书 API 返回错误 code={code}：{msgEl.GetString()}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "飞书渠道连通性测试失败 channel={ChannelId}", config.Id);
            return new ChannelTestResult(false, $"连接失败：{ex.Message}", sw.ElapsedMilliseconds);
        }
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
