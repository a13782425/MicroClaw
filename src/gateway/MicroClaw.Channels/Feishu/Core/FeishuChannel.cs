using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

internal sealed class FeishuChannelProvider(
    FeishuMessageProcessor processor,
    ILoggerFactory loggerFactory,
    FeishuChannelHealthStore healthStore,
    FeishuChannelStatsService statsService,
    FeishuWebSocketManager? wsManager = null,
    FeishuTokenCache? tokenCache = null) : IChannelProvider
{
    public string Name => "Feishu";

    public ChannelType Type => ChannelType.Feishu;

    public string DisplayName => "飞书";

    public IChannel Create(ChannelEntity config)
        => new FeishuChannel(config, this, processor, loggerFactory.CreateLogger<FeishuChannel>());

    public Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default)
        => Create(config).PublishAsync(message, cancellationToken);

    public Task<WebhookResult> HandleWebhookAsync(ChannelEntity config, string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
        => Create(config).HandleWebhookAsync(body, headers, cancellationToken);

    public Task<ChannelTestResult> TestConnectionAsync(ChannelEntity config, CancellationToken cancellationToken = default)
        => Create(config).TestConnectionAsync(cancellationToken);

    public Task<ChannelDiagnostics> GetDiagnosticsAsync(ChannelEntity config, CancellationToken cancellationToken = default)
    {
        FeishuChannelSettings? settings = FeishuChannelSettings.TryParse(config.SettingJson);
        string connectionMode = settings?.ConnectionMode ?? "webhook";

        string connectionStatus;
        if (!config.IsEnabled)
            connectionStatus = "disabled";
        else if (string.Equals(connectionMode, "websocket", StringComparison.OrdinalIgnoreCase))
            connectionStatus = wsManager?.GetConnectionStatus(config.Id) ?? "unknown";
        else
            connectionStatus = "webhook";

        TimeSpan? tokenTtl = settings?.AppId is not null
            ? tokenCache?.GetRemainingTtl(settings.AppId)
            : null;

        var (lastAt, lastSuccess, lastError) = healthStore.GetLastMessage(config.Id);
        var (sigFail, aiFail, replyFail) = statsService.GetStats(config.Id);

        Dictionary<string, object?> extra = new()
        {
            ["connectionMode"] = connectionMode,
            ["connectionStatus"] = connectionStatus,
            ["tokenRemainingSeconds"] = tokenTtl.HasValue ? (object?)Math.Round(tokenTtl.Value.TotalSeconds) : null,
            ["lastMessageAt"] = lastAt,
            ["lastMessageSuccess"] = lastSuccess,
            ["lastMessageError"] = lastError,
            ["signatureFailures"] = sigFail,
            ["aiCallFailures"] = aiFail,
            ["replyFailures"] = replyFail,
        };

        return Task.FromResult(new ChannelDiagnostics(config.Id, "feishu", connectionStatus, extra));
    }

    /// <summary>记录 Webhook 签名验证失败（由 <see cref="FeishuChannel"/> 内部调用）。</summary>
    internal void ReportSignatureFailure(string channelId) => statsService.IncrementSignatureFailure(channelId);
}

internal sealed class FeishuChannel(
    ChannelEntity config,
    FeishuChannelProvider provider,
    FeishuMessageProcessor processor,
    ILogger<FeishuChannel> logger) : IChannel
{
    public string Id => Config.Id;

    public string Name => "Feishu";

    public ChannelType Type => ChannelType.Feishu;

    public ChannelEntity Config { get; } = config;

    public string DisplayName => string.IsNullOrWhiteSpace(Config.DisplayName) ? "飞书" : Config.DisplayName;

    public Task<ChannelDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => provider.GetDiagnosticsAsync(Config, cancellationToken);

    public Task<string?> HandleSessionMessageAsync(SessionMessage message, SessionMessageContext context,
        CancellationToken cancellationToken = default)
        => ((IChannelProvider)provider).HandleSessionMessageAsync(Config, message, context, cancellationToken);

    public async Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(Config.SettingJson) ?? new();
        await processor.SendMessageAsync(message.UserId, message.Content, settings, cancellationToken);
    }

    public async Task<WebhookResult> HandleWebhookAsync(string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken ct = default)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(Config.SettingJson) ?? new();
        logger.LogDebug("飞书 Webhook 收到请求 channel={ChannelId}", Config.Id);

        // 签名验证（仅在配置了 EncryptKey 且请求携带 headers 时启用）
        if (!string.IsNullOrWhiteSpace(settings.EncryptKey) && headers is not null)
        {
            headers.TryGetValue("X-Lark-Signature", out string? signature);
            headers.TryGetValue("X-Lark-Request-Timestamp", out string? timestamp);
            headers.TryGetValue("X-Lark-Request-Nonce", out string? nonce);

            if (!IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning("飞书 Webhook 时间戳过期或无效 channel={ChannelId} timestamp={Timestamp}",
                    Config.Id, timestamp);
                provider.ReportSignatureFailure(Config.Id);
                return WebhookResult.Unauthorized("Timestamp expired or invalid");
            }

            if (!VerifyWebhookSignature(timestamp, nonce, settings.EncryptKey, body, signature))
            {
                logger.LogWarning("飞书 Webhook 签名验证失败 channel={ChannelId}", Config.Id);
                provider.ReportSignatureFailure(Config.Id);
                return WebhookResult.Unauthorized("Signature verification failed");
            }
        }

        string decrypted = TryDecrypt(body, settings.EncryptKey);

        FeishuUrlVerificationRequest? verification = TryDeserialize<FeishuUrlVerificationRequest>(decrypted);
        if (verification?.Type == "url_verification")
        {
            logger.LogInformation("飞书 URL 验证 channel={ChannelId}", Config.Id);
            return WebhookResult.Ok(JsonSerializer.Serialize(new { challenge = verification.Challenge }));
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

        return WebhookResult.Ok(JsonSerializer.Serialize(new { code = 0, msg = "ok" }));
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

    internal static bool VerifyWebhookSignature(string? timestamp, string? nonce, string encryptKey, string body, string? expectedSignature)
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

    internal static bool IsTimestampFresh(string? timestamp, int toleranceSeconds)
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
