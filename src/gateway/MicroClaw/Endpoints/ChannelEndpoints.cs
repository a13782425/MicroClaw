using System.Text.Json;
using MicroClaw.Channels;
using MicroClaw.Channels.Feishu;
using MicroClaw.Channels.Models;
using MicroClaw.Channels.WeCom;
using MicroClaw.Channels.WeChat;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Providers;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Endpoints;

public static class ChannelEndpoints
{
    public static IEndpointRouteBuilder MapChannelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/channels", (ChannelConfigStore store) =>
        {
            var result = store.All.Select(c => new
            {
                c.Id,
                c.DisplayName,
                ChannelType = ChannelConfigStore.SerializeChannelType(c.ChannelType),
                c.ProviderId,
                c.IsEnabled,
                Settings = ChannelConfigStore.MaskSettingsSecrets(c.SettingsJson, c.ChannelType)
            });
            return Results.Ok(result);
        })
        .WithTags("Channels");

        // 渠道类型列表（动态注入 IEnumerable<IChannel>，支持插件扩展）
        endpoints.MapGet("/channels/types", (IEnumerable<IChannel> registeredChannels) =>
        {
            // Web 是内置渠道，没有对应的 IChannel 实现，始终首位展示
            var types = new List<object>
            {
                new { type = "web", displayName = "Web（内置）", canCreate = false }
            };
            types.AddRange(registeredChannels.Select(c => (object)new
            {
                type = c.Type.ToString().ToLowerInvariant(),
                displayName = c.DisplayName,
                canCreate = c.CanCreate
            }));
            return Results.Ok(types);
        })
        .WithTags("Channels");

        // 渠道专属工具列表（支持插件注入的自定义渠道工具）
        endpoints.MapGet("/channels/{channelType}/tools", (
            string channelType,
            IEnumerable<IChannelToolProvider> toolProviders) =>
        {
            IChannelToolProvider? provider = toolProviders.FirstOrDefault(
                p => string.Equals(p.ChannelType.ToString(), channelType, StringComparison.OrdinalIgnoreCase));
            var tools = (provider?.GetToolDescriptions() ?? []).Select(t => new
            {
                name = t.Name,
                description = t.Description
            });
            return Results.Ok(tools);
        })
        .WithTags("Channels");

        endpoints.MapPost("/channels", (ChannelCreateRequest req, ChannelConfigStore store, ProviderConfigStore providerStore) =>
        {
            if (string.IsNullOrWhiteSpace(req.DisplayName))
                return ApiErrors.BadRequest("DisplayName is required.");
            if (string.IsNullOrWhiteSpace(req.ProviderId))
                return ApiErrors.BadRequest("ProviderId is required.");

            // 验证 Provider 存在
            if (providerStore.All.All(p => p.Id != req.ProviderId))
                return ApiErrors.BadRequest($"Provider '{req.ProviderId}' not found.");

            ChannelConfig config = new()
            {
                DisplayName = req.DisplayName.Trim(),
                ChannelType = ChannelConfigStore.ParseChannelType(req.ChannelType),
                ProviderId = req.ProviderId.Trim(),
                IsEnabled = req.IsEnabled,
                SettingsJson = req.Settings ?? "{}"
            };

            ChannelConfig created = store.Add(config);
            return Results.Ok(new { created.Id });
        })
        .WithTags("Channels");

        endpoints.MapPost("/channels/update", (ChannelUpdateRequest req, ChannelConfigStore store, ProviderConfigStore providerStore) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return ApiErrors.BadRequest("Id is required.");

            // 验证 Provider 存在（如果指定了新的 ProviderId）
            if (!string.IsNullOrWhiteSpace(req.ProviderId) && providerStore.All.All(p => p.Id != req.ProviderId))
                return ApiErrors.BadRequest($"Provider '{req.ProviderId}' not found.");

            ChannelConfig incoming = new()
            {
                DisplayName = req.DisplayName?.Trim() ?? string.Empty,
                ChannelType = ChannelConfigStore.ParseChannelType(req.ChannelType),
                ProviderId = req.ProviderId?.Trim() ?? string.Empty,
                IsEnabled = req.IsEnabled,
                SettingsJson = req.Settings ?? "{}"
            };

            ChannelConfig? updated = store.Update(req.Id, incoming);
            if (updated is null)
                return ApiErrors.NotFound($"Channel '{req.Id}' not found.");

            return Results.Ok(new { updated.Id });
        })
        .WithTags("Channels");

        endpoints.MapPost("/channels/delete", (ChannelDeleteRequest req, ChannelConfigStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return ApiErrors.BadRequest("Id is required.");

            bool deleted = store.Delete(req.Id);
            if (!deleted)
                return ApiErrors.NotFound($"Channel '{req.Id}' not found.");

            return Results.Ok();
        })
        .WithTags("Channels");

        endpoints.MapPost("/channels/{id}/test", async (
            string id,
            ChannelConfigStore store,
            IEnumerable<IChannel> channels,
            CancellationToken ct) =>
        {
            ChannelConfig? config = store.GetById(id);
            if (config is null)
                return ApiErrors.NotFound($"Channel '{id}' not found.");

            // Web 渠道为内部渠道，无需外部连通性测试
            if (config.ChannelType == ChannelType.Web)
                return Results.Ok(new ChannelTestResult(true, "Web 渠道无需外部连接测试", 0L));

            IChannel? channel = channels.FirstOrDefault(c => c.Type == config.ChannelType);
            if (channel is null)
                return ApiErrors.BadRequest($"未找到类型 '{config.ChannelType}' 的渠道服务实现。");

            ChannelTestResult result = await channel.TestConnectionAsync(config, ct);
            return Results.Ok(result);
        })
        .WithTags("Channels");

        endpoints.MapPost("/channels/{id}/publish", async (
            string id,
            ChannelPublishRequest req,
            ChannelConfigStore store,
            IEnumerable<IChannel> channels,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.TargetId))
                return ApiErrors.BadRequest("TargetId is required.");
            if (string.IsNullOrWhiteSpace(req.Content))
                return ApiErrors.BadRequest("Content is required.");

            ChannelConfig? config = store.GetById(id);
            if (config is null || !config.IsEnabled)
                return ApiErrors.NotFound($"Channel '{id}' not found or disabled.");

            IChannel? channel = channels.FirstOrDefault(c => c.Type == config.ChannelType);
            if (channel is null)
                return ApiErrors.BadRequest($"未找到类型 '{config.ChannelType}' 的渠道服务实现。");

            ChannelMessage message = new(req.TargetId.Trim(), req.Content, DateTimeOffset.UtcNow);
            await channel.PublishAsync(message, ct);
            return Results.Ok();
        })
        .WithTags("Channels");

        // F-F-2: 渠道健康检查端点
        endpoints.MapGet("/channels/{id}/health", (
            string id,
            ChannelConfigStore store,
            FeishuWebSocketManager? wsManager = null,
            FeishuTokenCache? tokenCache = null,
            FeishuChannelHealthStore? healthStore = null) =>
        {
            ChannelConfig? config = store.GetById(id);
            if (config is null)
                return ApiErrors.NotFound($"Channel '{id}' not found.");

            // 非飞书渠道只返回基础状态
            if (config.ChannelType != ChannelType.Feishu)
                return Results.Ok(new { ChannelId = id, ChannelType = config.ChannelType.ToString(), Status = "ok" });

            FeishuChannelSettings? settings = FeishuChannelSettings.TryParse(config.SettingsJson);
            string connectionMode = settings?.ConnectionMode ?? "webhook";

            string connectionStatus;
            if (!config.IsEnabled)
                connectionStatus = "disabled";
            else if (string.Equals(connectionMode, "websocket", StringComparison.OrdinalIgnoreCase))
                connectionStatus = wsManager?.GetConnectionStatus(id) ?? "unknown";
            else
                connectionStatus = "webhook"; // Webhook 模式无长连接状态

            TimeSpan? tokenTtl = settings?.AppId is not null
                ? tokenCache?.GetRemainingTtl(settings.AppId)
                : null;

            var (lastAt, lastSuccess, lastError) = healthStore is not null
                ? healthStore.GetLastMessage(id)
                : ((DateTimeOffset?)null, (bool?)null, (string?)null);

            return Results.Ok(new
            {
                ChannelId = id,
                ConnectionMode = connectionMode,
                ConnectionStatus = connectionStatus,
                TokenRemainingSeconds = tokenTtl.HasValue ? (long?)Math.Round(tokenTtl.Value.TotalSeconds) : null,
                LastMessageAt = lastAt,
                LastMessageSuccess = lastSuccess,
                LastMessageError = lastError
            });
        })
        .WithTags("Channels");

        // F-F-3: 渠道错误事件统计端点
        endpoints.MapGet("/channels/{id}/stats", (
            string id,
            ChannelConfigStore store,
            FeishuChannelStatsService? statsService = null) =>
        {
            ChannelConfig? config = store.GetById(id);
            if (config is null)
                return ApiErrors.NotFound($"Channel '{id}' not found.");

            // 非飞书渠道只返回基础状态
            if (config.ChannelType != ChannelType.Feishu)
                return Results.Ok(new { ChannelId = id, ChannelType = config.ChannelType.ToString(), Status = "ok" });

            var (sigFail, aiFail, replyFail) = statsService is not null
                ? statsService.GetStats(id)
                : (0L, 0L, 0L);

            return Results.Ok(new
            {
                ChannelId = id,
                SignatureFailures = sigFail,
                AiCallFailures = aiFail,
                ReplyFailures = replyFail
            });
        })
        .WithTags("Channels");

        return endpoints;
    }

    public static IEndpointRouteBuilder MapChannelWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/channels/feishu/{channelId}/webhook", async (
            string channelId,
            HttpContext context,
            ChannelConfigStore store,
            IEnumerable<IChannel> channels,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook");
            // F-F-3: 从 DI 容器解析统计服务（可选，未注册时为 null）
            FeishuChannelStatsService? statsService =
                context.RequestServices.GetService(typeof(FeishuChannelStatsService)) as FeishuChannelStatsService;
            logger.LogInformation("收到飞书 Webhook 请求 channelId={ChannelId}", channelId);

            ChannelConfig? config = store.GetById(channelId);
            if (config is null || !config.IsEnabled || config.ChannelType != ChannelType.Feishu)
            {
                logger.LogWarning("渠道未找到或已禁用 channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            IChannel? feishuChannel = channels.FirstOrDefault(c => c.Type == ChannelType.Feishu);
            if (feishuChannel is null)
            {
                logger.LogError("飞书渠道服务未注册");
                return Results.StatusCode(503);
            }

            using StreamReader reader = new(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            logger.LogDebug("飞书 Webhook body: {Body}", body);

            // 签名验证（仅在配置了 EncryptKey 时启用）
            FeishuChannelSettings settings = FeishuChannelSettings.TryParse(config.SettingsJson) ?? new();
            if (!string.IsNullOrWhiteSpace(settings.EncryptKey))
            {
                string? signature = context.Request.Headers["X-Lark-Signature"];
                string? timestamp = context.Request.Headers["X-Lark-Request-Timestamp"];
                string? nonce = context.Request.Headers["X-Lark-Request-Nonce"];

                if (!FeishuChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
                {
                    logger.LogWarning("飞书 Webhook 时间戳过期或无效 channelId={ChannelId} timestamp={Timestamp}",
                        channelId, timestamp);
                    // F-F-3: 签名验证失败计数
                    statsService?.IncrementSignatureFailure(channelId);
                    return Results.Json(new { success = false, message = "Timestamp expired or invalid" }, statusCode: 401);
                }

                if (!FeishuChannel.VerifyWebhookSignature(timestamp, nonce, settings.EncryptKey, body, signature))
                {
                    logger.LogWarning("飞书 Webhook 签名验证失败 channelId={ChannelId}", channelId);
                    // F-F-3: 签名验证失败计数
                    statsService?.IncrementSignatureFailure(channelId);
                    return Results.Json(new { success = false, message = "Signature verification failed" }, statusCode: 401);
                }
            }

            string? response = await feishuChannel.HandleWebhookAsync(body, config, context.RequestAborted);
            if (response is null)
                return Results.Ok();

            return Results.Content(response, "application/json");
        })
        .WithTags("Webhooks");

        // ─── 企业微信（WeCom）Webhook ───────────────────────────────────────────

        // GET：URL 接入验证（企微后台配置时发送，返回 echostr）
        endpoints.MapGet("/channels/wecom/{channelId}/webhook", (
            string channelId,
            HttpContext context,
            ChannelConfigStore store,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeCom");

            ChannelConfig? config = store.GetById(channelId);
            if (config is null || !config.IsEnabled || config.ChannelType != ChannelType.WeCom)
            {
                logger.LogWarning("企业微信渠道未找到或已禁用 channelId={ChannelId}", channelId);
                return Results.NotFound();
            }

            WeComChannelSettings settings = WeComChannelSettings.TryParse(config.SettingsJson) ?? new();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                logger.LogWarning("企业微信渠道未配置 Token，拒绝 URL 验证 channelId={ChannelId}", channelId);
                return Results.BadRequest();
            }

            string? msgSignature = context.Request.Query["msg_signature"];
            string? timestamp    = context.Request.Query["timestamp"];
            string? nonce        = context.Request.Query["nonce"];
            string? echostr      = context.Request.Query["echostr"];

            if (!WeComChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning("企业微信 URL 验证时间戳过期 channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            // URL 验证阶段无 msg_encrypt，使用基础三字段签名
            if (!WeComChannel.VerifySignature(settings.Token, timestamp, nonce, msgSignature))
            {
                logger.LogWarning("企业微信 URL 验证签名失败 channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            logger.LogInformation("企业微信 URL 验证成功 channelId={ChannelId}", channelId);
            return Results.Text(echostr ?? string.Empty);
        })
        .WithTags("Webhooks");

        // POST：消息与事件回调（验证签名后转发至 HandleWebhookAsync）
        endpoints.MapPost("/channels/wecom/{channelId}/webhook", async (
            string channelId,
            HttpContext context,
            ChannelConfigStore store,
            IEnumerable<IChannel> channels,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeCom");
            logger.LogInformation("收到企业微信 Webhook 请求 channelId={ChannelId}", channelId);

            ChannelConfig? config = store.GetById(channelId);
            if (config is null || !config.IsEnabled || config.ChannelType != ChannelType.WeCom)
            {
                logger.LogWarning("企业微信渠道未找到或已禁用 channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            if (string.IsNullOrWhiteSpace(WeComChannelSettings.TryParse(config.SettingsJson)?.Token))
            {
                logger.LogWarning("企业微信渠道未配置 Token channelId={ChannelId}", channelId);
                return ApiErrors.BadRequest("Token is not configured.");
            }

            WeComChannelSettings settings = WeComChannelSettings.TryParse(config.SettingsJson)!;

            string? msgSignature = context.Request.Query["msg_signature"];
            string? timestamp    = context.Request.Query["timestamp"];
            string? nonce        = context.Request.Query["nonce"];

            if (!WeComChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning("企业微信 Webhook 时间戳过期 channelId={ChannelId}", channelId);
                return Results.Json(new { success = false, message = "Timestamp expired or invalid" }, statusCode: 401);
            }

            using StreamReader reader = new(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            logger.LogDebug("企业微信 Webhook body: {Body}", body);

            // 从 XML 中提取 Encrypt 字段用于签名（SafeMode）；明文模式下 msgEncrypt 为 null
            string? msgEncrypt = ExtractXmlField(body, "Encrypt");

            if (!WeComChannel.VerifySignature(settings.Token, timestamp, nonce, msgSignature, msgEncrypt))
            {
                logger.LogWarning("企业微信 Webhook 签名验证失败 channelId={ChannelId}", channelId);
                return Results.Json(new { success = false, message = "Signature verification failed" }, statusCode: 401);
            }

            IChannel? weComChannel = channels.FirstOrDefault(c => c.Type == ChannelType.WeCom);
            if (weComChannel is null)
            {
                logger.LogError("企业微信渠道服务未注册");
                return Results.StatusCode(503);
            }

            string? response = await weComChannel.HandleWebhookAsync(body, config, context.RequestAborted);
            return response is null ? Results.Ok() : Results.Content(response, "application/xml");
        })
        .WithTags("Webhooks");

        // ─── 微信公众号（WeChat）Webhook ────────────────────────────────────────

        // GET：URL 接入验证
        endpoints.MapGet("/channels/wechat/{channelId}/webhook", (
            string channelId,
            HttpContext context,
            ChannelConfigStore store,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeChat");

            ChannelConfig? config = store.GetById(channelId);
            if (config is null || !config.IsEnabled || config.ChannelType != ChannelType.WeChat)
            {
                logger.LogWarning("微信渠道未找到或已禁用 channelId={ChannelId}", channelId);
                return Results.NotFound();
            }

            WeChatChannelSettings settings = WeChatChannelSettings.TryParse(config.SettingsJson) ?? new();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                logger.LogWarning("微信渠道未配置 Token，拒绝 URL 验证 channelId={ChannelId}", channelId);
                return Results.BadRequest();
            }

            string? signature = context.Request.Query["signature"];
            string? timestamp = context.Request.Query["timestamp"];
            string? nonce     = context.Request.Query["nonce"];
            string? echostr   = context.Request.Query["echostr"];

            if (!WeChatChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning("微信 URL 验证时间戳过期 channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            if (!WeChatChannel.VerifySignature(settings.Token, timestamp, nonce, signature))
            {
                logger.LogWarning("微信 URL 验证签名失败 channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            logger.LogInformation("微信 URL 验证成功 channelId={ChannelId}", channelId);
            return Results.Text(echostr ?? string.Empty);
        })
        .WithTags("Webhooks");

        // POST：消息与事件回调
        endpoints.MapPost("/channels/wechat/{channelId}/webhook", async (
            string channelId,
            HttpContext context,
            ChannelConfigStore store,
            IEnumerable<IChannel> channels,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeChat");
            logger.LogInformation("收到微信 Webhook 请求 channelId={ChannelId}", channelId);

            ChannelConfig? config = store.GetById(channelId);
            if (config is null || !config.IsEnabled || config.ChannelType != ChannelType.WeChat)
            {
                logger.LogWarning("微信渠道未找到或已禁用 channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            WeChatChannelSettings settings = WeChatChannelSettings.TryParse(config.SettingsJson) ?? new();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                logger.LogWarning("微信渠道未配置 Token channelId={ChannelId}", channelId);
                return ApiErrors.BadRequest("Token is not configured.");
            }

            // 安全模式下使用 msg_signature，明文模式使用 signature
            string? msgSignature = context.Request.Query["msg_signature"];
            string? signature    = context.Request.Query["signature"];
            string? timestamp    = context.Request.Query["timestamp"];
            string? nonce        = context.Request.Query["nonce"];

            if (!WeChatChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning("微信 Webhook 时间戳过期 channelId={ChannelId}", channelId);
                return Results.Json(new { success = false, message = "Timestamp expired or invalid" }, statusCode: 401);
            }

            using StreamReader reader = new(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            logger.LogDebug("微信 Webhook body: {Body}", body);

            if (!string.IsNullOrEmpty(msgSignature))
            {
                // 安全模式：从 XML 中提取 Encrypt 字段
                string? msgEncrypt = ExtractXmlField(body, "Encrypt");
                if (!WeChatChannel.VerifySignature(settings.Token, timestamp, nonce, msgSignature, msgEncrypt))
                {
                    logger.LogWarning("微信 Webhook 签名验证失败（安全模式） channelId={ChannelId}", channelId);
                    return Results.Json(new { success = false, message = "Signature verification failed" }, statusCode: 401);
                }
            }
            else
            {
                // 明文模式：仅三字段签名
                if (!WeChatChannel.VerifySignature(settings.Token, timestamp, nonce, signature))
                {
                    logger.LogWarning("微信 Webhook 签名验证失败（明文模式） channelId={ChannelId}", channelId);
                    return Results.Json(new { success = false, message = "Signature verification failed" }, statusCode: 401);
                }
            }

            IChannel? weChatChannel = channels.FirstOrDefault(c => c.Type == ChannelType.WeChat);
            if (weChatChannel is null)
            {
                logger.LogError("微信渠道服务未注册");
                return Results.StatusCode(503);
            }

            string? response = await weChatChannel.HandleWebhookAsync(body, config, context.RequestAborted);
            return response is null ? Results.Ok() : Results.Content(response, "application/xml");
        })
        .WithTags("Webhooks");

        return endpoints;
    }

    /// <summary>从 XML 字符串中提取指定标签内的文本内容（简单字符串解析，无需完整 XML 解析器）。</summary>
    private static string? ExtractXmlField(string xml, string tagName)
    {
        string open  = $"<{tagName}>";
        string close = $"</{tagName}>";
        int start = xml.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return null;
        start += open.Length;
        int end = xml.IndexOf(close, start, StringComparison.Ordinal);
        return end < 0 ? null : xml[start..end].Trim();
    }
}

public sealed record ChannelCreateRequest(
    string DisplayName,
    string ChannelType,
    string ProviderId,
    bool IsEnabled = true,
    string? Settings = null);

public sealed record ChannelUpdateRequest(
    string Id,
    string? DisplayName,
    string? ChannelType,
    string? ProviderId,
    bool IsEnabled = true,
    string? Settings = null);

public sealed record ChannelDeleteRequest(string Id);

public sealed record ChannelPublishRequest(string TargetId, string Content);
