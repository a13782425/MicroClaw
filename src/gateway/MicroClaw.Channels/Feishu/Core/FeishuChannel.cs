using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FeishuNetSdk;
using FeishuNetSdk.Services;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 自包含飞书渠道实例：一个实例 = 一个飞书应用 + 一个 Agent 绑定。
/// 持有独立 SDK ServiceProvider（含 <see cref="IFeishuTenantApi"/>）及可选 WssService（WebSocket 模式）。
/// 实例通过 <see cref="CreateAsync"/> 工厂方法创建，通过 <see cref="DisposeAsync"/> 销毁。
/// </summary>
internal sealed class FeishuChannel : IChannel, IAsyncDisposable
{
    private readonly ServiceProvider _sp;
    private readonly IHostedService? _wssService;
    private readonly CancellationTokenSource _cts = new();
    private readonly FeishuChannelProvider _provider;
    private readonly ILogger<FeishuChannel> _logger;

    /// <summary>该渠道对应的 <see cref="IFeishuTenantApi"/>，与子 SP 生命周期相同。</summary>
    public IFeishuTenantApi Api { get; }

    private FeishuChannel(
        ChannelEntity config,
        ServiceProvider sp,
        IFeishuTenantApi api,
        IHostedService? wssService,
        FeishuChannelProvider provider,
        ILogger<FeishuChannel> logger)
    {
        Config = config;
        _sp = sp;
        Api = api;
        _wssService = wssService;
        _provider = provider;
        _logger = logger;
    }

    /// <summary>
    /// Phase 1 lightweight factory: creates channel without message processor.
    /// Full SDK integration, but no message handling until Phase 2/3.
    /// </summary>
    /// <summary>
    /// 创建飞书渠道实例，为该渠道独立构建 SDK ServiceProvider。
    /// WebSocket 模式下同时注册并启动 <c>WssService</c>；Webhook 模式下仅注册 API 客户端。
    /// </summary>
    internal static async Task<FeishuChannel> CreateAsync(
        ChannelEntity config,
        FeishuChannelSettings settings,
        FeishuChannelProvider provider,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default)
    {
        ServiceCollection services = new();

        // Support configurable API Base URL
        Action<HttpClient>? configureHttpClient = null;
        if (!string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
            && !settings.ApiBaseUrl.Equals("https://open.feishu.cn", StringComparison.OrdinalIgnoreCase))
        {
            string baseUrl = settings.ApiBaseUrl.TrimEnd('/');
            configureHttpClient = client => client.BaseAddress = new Uri(baseUrl);
        }

        services.AddFeishuNetSdk(
            appId: settings.AppId,
            appSecret: settings.AppSecret,
            encryptKey: settings.EncryptKey,
            verificationToken: settings.VerificationToken,
            httpClientOptions: configureHttpClient);

        bool isWebSocket = string.Equals(settings.ConnectionMode, "websocket", StringComparison.OrdinalIgnoreCase);

        if (isWebSocket)
        {
            services.AddFeishuWebSocket();
            services.AddSingleton(new FeishuChannelContext(config, settings));
            // Share the logger factory with the child SP
            services.AddSingleton(loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            services.AddSingleton(provider.Processor); 
            // Phase 2/3: register FeishuMessageEventHandler here when processor is available
        }

        ServiceProvider sp = services.BuildServiceProvider();

        // Resolve TenantApi and inject it into FeishuChannelContext so event handler can use it
        IFeishuTenantApi tenantApi = sp.GetRequiredService<IFeishuTenantApi>();
        if (isWebSocket)
        {
            FeishuChannelContext ctx = sp.GetRequiredService<FeishuChannelContext>();
            ctx.SetApi(tenantApi);
        }

        IHostedService? wssService = null;
        if (isWebSocket)
        {
            IHostedService[] hostedServices = sp.GetServices<IHostedService>().ToArray();
            foreach (IHostedService svc in hostedServices)
                await svc.StartAsync(ct);

            // Use the first BackgroundService (WssService) as the representative
            wssService = hostedServices.OfType<BackgroundService>().FirstOrDefault()
                         ?? hostedServices.FirstOrDefault();
        }

        var logger = loggerFactory.CreateLogger<FeishuChannel>();
        return new FeishuChannel(config, sp, tenantApi, wssService, provider, logger);
    }

    public string Id => Config.Id;
    public string Name => "Feishu";
    public ChannelType Type => ChannelType.Feishu;
    public ChannelEntity Config { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(Config.DisplayName) ? "飞书" : Config.DisplayName;

    public Task<ChannelDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => _provider.GetDiagnosticsAsync(Config, cancellationToken);

    public Task<string?> HandleSessionMessageAsync(SessionMessage message, SessionMessageContext context,
        CancellationToken cancellationToken = default)
        => ((IChannelProvider)_provider).HandleSessionMessageAsync(Config, message, context, cancellationToken);

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        // Phase 2/3: delegate to FeishuMessageProcessor.SendMessageAsync
        _logger.LogWarning("飞书渠道消息发送跳过：Processor 未初始化（Phase 1） channel={ChannelId}", Config.Id);
        return Task.CompletedTask;
    }

    public async Task<WebhookResult> HandleWebhookAsync(string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken ct = default)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(Config.SettingJson) ?? new();
        _logger.LogDebug("飞书 Webhook 收到请求 channel={ChannelId}", Config.Id);

        // 签名验证（仅在配置了 EncryptKey 且请求携带 headers 时启用）
        if (!string.IsNullOrWhiteSpace(settings.EncryptKey) && headers is not null)
        {
            headers.TryGetValue("X-Lark-Signature", out string? signature);
            headers.TryGetValue("X-Lark-Request-Timestamp", out string? timestamp);
            headers.TryGetValue("X-Lark-Request-Nonce", out string? nonce);

            if (!IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                _logger.LogWarning("飞书 Webhook 时间戳过期或无效 channel={ChannelId} timestamp={Timestamp}",
                    Config.Id, timestamp);
                _provider.ReportSignatureFailure(Config.Id);
                return WebhookResult.Unauthorized("Timestamp expired or invalid");
            }

            if (!VerifyWebhookSignature(timestamp, nonce, settings.EncryptKey, body, signature))
            {
                _logger.LogWarning("飞书 Webhook 签名验证失败 channel={ChannelId}", Config.Id);
                _provider.ReportSignatureFailure(Config.Id);
                return WebhookResult.Unauthorized("Signature verification failed");
            }
        }

        string decrypted = TryDecrypt(body, settings.EncryptKey);

        FeishuUrlVerificationRequest? verification = TryDeserialize<FeishuUrlVerificationRequest>(decrypted);
        if (verification?.Type == "url_verification")
        {
            _logger.LogInformation("飞书 URL 验证 channel={ChannelId}", Config.Id);
            return WebhookResult.Ok(JsonSerializer.Serialize(new { challenge = verification.Challenge }));
        }

        FeishuEventCallback<FeishuMessageEvent>? callback = TryDeserialize<FeishuEventCallback<FeishuMessageEvent>>(decrypted);
        if (callback?.Header?.EventType == "im.message.receive_v1" && callback.Event is not null)
        {
            // Phase 2/3: delegate to FeishuMessageProcessor.ProcessMessageAsync
            _logger.LogInformation("飞书 Webhook 消息接收 channel={ChannelId}（Phase 1 不处理）", Config.Id);
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
                    _logger.LogInformation(
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
            _logger.LogWarning(ex, "飞书渠道连通性测试失败 channel={ChannelId}", Config.Id);
            return new ChannelTestResult(false, $"连接失败：{ex.Message}", sw.ElapsedMilliseconds);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_wssService is not null)
        {
            try { await _wssService.StopAsync(CancellationToken.None); }
            catch { /* best-effort */ }
        }
        _cts.Dispose();
        await _sp.DisposeAsync();
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
