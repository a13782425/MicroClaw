using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

public sealed class FeishuChannelProvider(
    FeishuMessageProcessor processor,
    ILoggerFactory loggerFactory) : IChannelProvider
{
    public string Name => "Feishu";

    public ChannelType Type => ChannelType.Feishu;

    public string DisplayName => "飞书";

    public IChannel Create(ChannelEntity config)
        => new FeishuChannel(config, processor, loggerFactory.CreateLogger<FeishuChannel>());

    public Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default)
        => Create(config).PublishAsync(message, cancellationToken);

    public Task<string?> HandleWebhookAsync(ChannelEntity config, string body, CancellationToken cancellationToken = default)
        => Create(config).HandleWebhookAsync(body, cancellationToken);

    public Task<ChannelTestResult> TestConnectionAsync(ChannelEntity config, CancellationToken cancellationToken = default)
        => Create(config).TestConnectionAsync(cancellationToken);
}

public sealed class FeishuChannel(
    ChannelEntity config,
    FeishuMessageProcessor processor,
    ILogger<FeishuChannel> logger) : IChannel
{
    public string Id => Config.Id;

    public string Name => "Feishu";

    public ChannelType Type => ChannelType.Feishu;

    public ChannelEntity Config { get; } = config;

    public string DisplayName => string.IsNullOrWhiteSpace(Config.DisplayName) ? "飞书" : Config.DisplayName;

    public async Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(Config.SettingJson) ?? new();
        await processor.SendMessageAsync(message.UserId, message.Content, settings, cancellationToken);
    }

    public async Task<string?> HandleWebhookAsync(string body, CancellationToken ct = default)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(Config.SettingJson) ?? new();
        logger.LogDebug("飞书 Webhook 收到请求 channel={ChannelId}", Config.Id);

        string decrypted = TryDecrypt(body, settings.EncryptKey);

        FeishuUrlVerificationRequest? verification = TryDeserialize<FeishuUrlVerificationRequest>(decrypted);
        if (verification?.Type == "url_verification")
        {
            logger.LogInformation("飞书 URL 验证 channel={ChannelId}", Config.Id);
            return JsonSerializer.Serialize(new { challenge = verification.Challenge });
        }

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

                string traceId = messageId.Length >= 8 ? messageId[..8] : messageId;
                logger.LogInformation(
                    "[{TraceId}] Webhook 接收 channel={ChannelId} from={SenderId} messageId={MessageId}",
                    traceId, Config.Id, senderId, messageId);

                string chatType = callback.Event.Message.ChatType ?? "p2p";
                IReadOnlyList<string> mentionedOpenIds = callback.Event.Message.Mentions is { Length: > 0 }
                    ? callback.Event.Message.Mentions
                        .Select(m => m.Id?.OpenId)
                        .OfType<string>()
                        .ToList()
                    : [];

                string? rootId = callback.Event.Message.RootId;
                _ = Task.Run(() => processor.ProcessMessageAsync(
                    userText, senderId, chatId, messageId, Config, settings,
                    chatType, mentionedOpenIds, rootId: rootId, ct: CancellationToken.None));
            }
        }

        return JsonSerializer.Serialize(new { code = 0, msg = "ok" });
    }

    public async Task<ChannelTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(Config.SettingJson) ?? new();

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
            {
                string? hint = null;
                if (settings.ConnectionMode.Equals("webhook", StringComparison.OrdinalIgnoreCase) &&
                    IsLikelyPrivateNetworkOnly())
                {
                    hint = "当前服务器看起来处于内网环境，飞书可能无法访问您的 Webhook 地址。" +
                           "建议将连接模式改为「WebSocket 长连接」，无需公网 IP 也可正常接收消息。";
                    logger.LogInformation(
                        "F-E-3 Webhook 内网探测提示 channel={ChannelId}", Config.Id);
                }
                return new ChannelTestResult(true, "连接成功", sw.ElapsedMilliseconds, hint);
            }

            doc.RootElement.TryGetProperty("msg", out JsonElement msgEl);
            return new ChannelTestResult(false, $"飞书 API 返回错误 code={code}：{msgEl.GetString()}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "飞书渠道连通性测试失败 channel={ChannelId}", Config.Id);
            return new ChannelTestResult(false, $"连接失败：{ex.Message}", sw.ElapsedMilliseconds);
        }
    }

    internal static bool IsLikelyPrivateNetworkOnly()
    {
        try
        {
            var publicIps = System.Net.NetworkInformation.NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(ni =>
                    ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                    ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Select(ua => ua.Address)
                .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Where(ip => !IsPrivateOrLinkLocalIpv4(ip))
                .ToList();

            return publicIps.Count == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPrivateOrLinkLocalIpv4(System.Net.IPAddress ip)
    {
        byte[] b = ip.GetAddressBytes();
        return b[0] == 10 ||
               (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
               (b[0] == 192 && b[1] == 168) ||
               (b[0] == 169 && b[1] == 254);
    }

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
