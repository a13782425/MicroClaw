using System.Text.Json;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Channels;
using MicroClaw.Channels.Feishu;
using MicroClaw.Channels.WeCom;
using MicroClaw.Channels.WeChat;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using MicroClaw.Tools;
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
                c.IsEnabled,
                Settings = ChannelConfigStore.MaskSettingsSecrets(c.SettingJson, c.ChannelType)
            });
            return Results.Ok(result);
        })
        .WithTags("Channels");

        // 渠道类型列表（动态注入 provider，支持插件扩展）
        endpoints.MapGet("/channels/types", (IChannelService channelService) =>
        {
            // Web 是内置渠道，始终首位展示
            var types = new List<object>
            {
                new { type = "web", displayName = "Web（内置）", canCreate = false }
            };
            types.AddRange(channelService.GetProviders()
                .Where(static provider => provider.Type != ChannelType.Web)
                .Select(c => (object)new
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
            IEnumerable<IToolProvider> toolProviders) =>
        {
            IToolProvider? provider = toolProviders.FirstOrDefault(
                p => p.Category == ToolCategory.Channel
                     && string.Equals(p.GroupId, channelType, StringComparison.OrdinalIgnoreCase));
            var tools = (provider?.GetToolDescriptions() ?? []).Select(t => new
            {
                name = t.Name,
                description = t.Description
            });
            return Results.Ok(tools);
        })
        .WithTags("Channels");

        endpoints.MapPost("/channels", (ChannelCreateRequest req, ChannelConfigStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.DisplayName))
                return ApiErrors.BadRequest("DisplayName is required.");

            ChannelEntity channel = new()
            {
                DisplayName = req.DisplayName.Trim(),
                ChannelType = ChannelConfigStore.ParseChannelType(req.ChannelType),
                IsEnabled = req.IsEnabled,
                SettingJson = req.Settings ?? "{}"
            };

            ChannelEntity created = store.Add(channel);
            return Results.Ok(new { created.Id });
        })
        .WithTags("Channels");

        endpoints.MapPost("/channels/update", (ChannelUpdateRequest req, ChannelConfigStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return ApiErrors.BadRequest("Id is required.");

            ChannelEntity incoming = new()
            {
                DisplayName = req.DisplayName?.Trim() ?? string.Empty,
                ChannelType = ChannelConfigStore.ParseChannelType(req.ChannelType),
                IsEnabled = req.IsEnabled,
                SettingJson = req.Settings ?? "{}"
            };

            ChannelEntity? updated = store.Update(req.Id, incoming);
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
            IChannelService channelService,
            CancellationToken ct) =>
        {
            if (!channelService.TryGet(id, out IChannel? channel))
                return ApiErrors.NotFound($"Channel '{id}' not found.");

            ChannelTestResult result = await channel.TestConnectionAsync(ct);
            return Results.Ok(result);
        })
        .WithTags("Channels");

        endpoints.MapPost("/channels/{id}/publish", async (
            string id,
            ChannelPublishRequest req,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.TargetId))
                return ApiErrors.BadRequest("TargetId is required.");
            if (string.IsNullOrWhiteSpace(req.Content))
                return ApiErrors.BadRequest("Content is required.");

            if (!channelService.TryGet(id, out IChannel? channel) || !channel.Config.IsEnabled)
                return ApiErrors.NotFound($"Channel '{id}' not found or disabled.");

            ChannelMessage message = new(req.TargetId.Trim(), req.Content, DateTimeOffset.UtcNow);
            await channel.PublishAsync(message, ct);
            return Results.Ok();
        })
        .WithTags("Channels");

        // F-F-2: 娓犻亾鍋ュ悍妫€鏌ョ鐐?
        endpoints.MapGet("/channels/{id}/health", (
            string id,
            ChannelConfigStore store,
            FeishuWebSocketManager? wsManager = null,
            FeishuTokenCache? tokenCache = null,
            FeishuChannelHealthStore? healthStore = null) =>
        {
            ChannelEntity? config = store.GetById(id);
            if (config is null)
                return ApiErrors.NotFound($"Channel '{id}' not found.");

            // 闈為涔︽笭閬撳彧杩斿洖鍩虹鐘舵€?
            if (config.ChannelType != ChannelType.Feishu)
                return Results.Ok(new { ChannelId = id, ChannelType = config.ChannelType.ToString(), Status = "ok" });

            FeishuChannelSettings? settings = FeishuChannelSettings.TryParse(config.SettingJson);
            string connectionMode = settings?.ConnectionMode ?? "webhook";

            string connectionStatus;
            if (!config.IsEnabled)
                connectionStatus = "disabled";
            else if (string.Equals(connectionMode, "websocket", StringComparison.OrdinalIgnoreCase))
                connectionStatus = wsManager?.GetConnectionStatus(id) ?? "unknown";
            else
                connectionStatus = "webhook"; // Webhook 妯″紡鏃犻暱杩炴帴鐘舵€?

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

        // F-F-3: 娓犻亾閿欒浜嬩欢缁熻绔偣
        endpoints.MapGet("/channels/{id}/stats", (
            string id,
            ChannelConfigStore store,
            FeishuChannelStatsService? statsService = null) =>
        {
            ChannelEntity? config = store.GetById(id);
            if (config is null)
                return ApiErrors.NotFound($"Channel '{id}' not found.");

            // 闈為涔︽笭閬撳彧杩斿洖鍩虹鐘舵€?
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
            IChannelService channelService,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook");
            // F-F-3: 浠?DI 瀹瑰櫒瑙ｆ瀽缁熻鏈嶅姟锛堝彲閫夛紝鏈敞鍐屾椂涓?null锛?
            FeishuChannelStatsService? statsService =
                context.RequestServices.GetService(typeof(FeishuChannelStatsService)) as FeishuChannelStatsService;
            logger.LogInformation("鏀跺埌椋炰功 Webhook 璇锋眰 channelId={ChannelId}", channelId);

            if (!channelService.TryGet(channelId, out IChannel? feishuChannel)
                || !feishuChannel.Config.IsEnabled
                || feishuChannel.Type != ChannelType.Feishu)
            {
                logger.LogWarning("娓犻亾鏈壘鍒版垨宸茬鐢?channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            using StreamReader reader = new(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            logger.LogDebug("椋炰功 Webhook body: {Body}", body);

            // 绛惧悕楠岃瘉锛堜粎鍦ㄩ厤缃簡 EncryptKey 鏃跺惎鐢級
            FeishuChannelSettings settings = FeishuChannelSettings.TryParse(feishuChannel.Config.SettingJson) ?? new();
            if (!string.IsNullOrWhiteSpace(settings.EncryptKey))
            {
                string? signature = context.Request.Headers["X-Lark-Signature"];
                string? timestamp = context.Request.Headers["X-Lark-Request-Timestamp"];
                string? nonce = context.Request.Headers["X-Lark-Request-Nonce"];

                if (!FeishuChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
                {
                    logger.LogWarning("椋炰功 Webhook 鏃堕棿鎴宠繃鏈熸垨鏃犳晥 channelId={ChannelId} timestamp={Timestamp}",
                        channelId, timestamp);
                    // F-F-3: 绛惧悕楠岃瘉澶辫触璁℃暟
                    statsService?.IncrementSignatureFailure(channelId);
                    return Results.Json(new { success = false, message = "Timestamp expired or invalid" }, statusCode: 401);
                }

                if (!FeishuChannel.VerifyWebhookSignature(timestamp, nonce, settings.EncryptKey, body, signature))
                {
                    logger.LogWarning("椋炰功 Webhook 绛惧悕楠岃瘉澶辫触 channelId={ChannelId}", channelId);
                    // F-F-3: 绛惧悕楠岃瘉澶辫触璁℃暟
                    statsService?.IncrementSignatureFailure(channelId);
                    return Results.Json(new { success = false, message = "Signature verification failed" }, statusCode: 401);
                }
            }

            string? response = await feishuChannel.HandleWebhookAsync(body, context.RequestAborted);
            if (response is null)
                return Results.Ok();

            return Results.Content(response, "application/json");
        })
        .WithTags("Webhooks");

        // 鈹€鈹€鈹€ 浼佷笟寰俊锛圵eCom锛塛ebhook 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        // GET锛歎RL 鎺ュ叆楠岃瘉锛堜紒寰悗鍙伴厤缃椂鍙戦€侊紝杩斿洖 echostr锛?
        endpoints.MapGet("/channels/wecom/{channelId}/webhook", (
            string channelId,
            HttpContext context,
            ChannelConfigStore store,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeCom");

            ChannelEntity? config = store.GetById(channelId);
            if (config is null || !config.IsEnabled || config.ChannelType != ChannelType.WeCom)
            {
                logger.LogWarning("浼佷笟寰俊娓犻亾鏈壘鍒版垨宸茬鐢?channelId={ChannelId}", channelId);
                return Results.NotFound();
            }

            WeComChannelSettings settings = WeComChannelSettings.TryParse(config.SettingJson) ?? new();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                logger.LogWarning("浼佷笟寰俊娓犻亾鏈厤缃?Token锛屾嫆缁?URL 楠岃瘉 channelId={ChannelId}", channelId);
                return Results.BadRequest();
            }

            string? msgSignature = context.Request.Query["msg_signature"];
            string? timestamp    = context.Request.Query["timestamp"];
            string? nonce        = context.Request.Query["nonce"];
            string? echostr      = context.Request.Query["echostr"];

            if (!WeComChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning("浼佷笟寰俊 URL 楠岃瘉鏃堕棿鎴宠繃鏈?channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            // URL 楠岃瘉闃舵鏃?msg_encrypt锛屼娇鐢ㄥ熀纭€涓夊瓧娈电鍚?
            if (!WeComChannel.VerifySignature(settings.Token, timestamp, nonce, msgSignature))
            {
                logger.LogWarning("浼佷笟寰俊 URL 楠岃瘉绛惧悕澶辫触 channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            logger.LogInformation("浼佷笟寰俊 URL 楠岃瘉鎴愬姛 channelId={ChannelId}", channelId);
            return Results.Text(echostr ?? string.Empty);
        })
        .WithTags("Webhooks");

        // POST锛氭秷鎭笌浜嬩欢鍥炶皟锛堥獙璇佺鍚嶅悗杞彂鑷?HandleWebhookAsync锛?
        endpoints.MapPost("/channels/wecom/{channelId}/webhook", async (
            string channelId,
            HttpContext context,
            IChannelService channelService,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeCom");
            logger.LogInformation("鏀跺埌浼佷笟寰俊 Webhook 璇锋眰 channelId={ChannelId}", channelId);

            if (!channelService.TryGet(channelId, out IChannel? weComChannel)
                || !weComChannel.Config.IsEnabled
                || weComChannel.Type != ChannelType.WeCom)
            {
                logger.LogWarning("浼佷笟寰俊娓犻亾鏈壘鍒版垨宸茬鐢?channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            if (string.IsNullOrWhiteSpace(WeComChannelSettings.TryParse(weComChannel.Config.SettingJson)?.Token))
            {
                logger.LogWarning("浼佷笟寰俊娓犻亾鏈厤缃?Token channelId={ChannelId}", channelId);
                return ApiErrors.BadRequest("Token is not configured.");
            }

            WeComChannelSettings settings = WeComChannelSettings.TryParse(weComChannel.Config.SettingJson)!;

            string? msgSignature = context.Request.Query["msg_signature"];
            string? timestamp    = context.Request.Query["timestamp"];
            string? nonce        = context.Request.Query["nonce"];

            if (!WeComChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning("浼佷笟寰俊 Webhook 鏃堕棿鎴宠繃鏈?channelId={ChannelId}", channelId);
                return Results.Json(new { success = false, message = "Timestamp expired or invalid" }, statusCode: 401);
            }

            using StreamReader reader = new(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            logger.LogDebug("浼佷笟寰俊 Webhook body: {Body}", body);

            // 浠?XML 涓彁鍙?Encrypt 瀛楁鐢ㄤ簬绛惧悕锛圫afeMode锛夛紱鏄庢枃妯″紡涓?msgEncrypt 涓?null
            string? msgEncrypt = ExtractXmlField(body, "Encrypt");

            if (!WeComChannel.VerifySignature(settings.Token, timestamp, nonce, msgSignature, msgEncrypt))
            {
                logger.LogWarning("浼佷笟寰俊 Webhook 绛惧悕楠岃瘉澶辫触 channelId={ChannelId}", channelId);
                return Results.Json(new { success = false, message = "Signature verification failed" }, statusCode: 401);
            }

            string? response = await weComChannel.HandleWebhookAsync(body, context.RequestAborted);
            return response is null ? Results.Ok() : Results.Content(response, "application/xml");
        })
        .WithTags("Webhooks");

        // 鈹€鈹€鈹€ 寰俊鍏紬鍙凤紙WeChat锛塛ebhook 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        // GET锛歎RL 鎺ュ叆楠岃瘉
        endpoints.MapGet("/channels/wechat/{channelId}/webhook", (
            string channelId,
            HttpContext context,
            ChannelConfigStore store,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeChat");

            ChannelEntity? config = store.GetById(channelId);
            if (config is null || !config.IsEnabled || config.ChannelType != ChannelType.WeChat)
            {
                logger.LogWarning("寰俊娓犻亾鏈壘鍒版垨宸茬鐢?channelId={ChannelId}", channelId);
                return Results.NotFound();
            }

            WeChatChannelSettings settings = WeChatChannelSettings.TryParse(config.SettingJson) ?? new();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                logger.LogWarning("寰俊娓犻亾鏈厤缃?Token锛屾嫆缁?URL 楠岃瘉 channelId={ChannelId}", channelId);
                return Results.BadRequest();
            }

            string? signature = context.Request.Query["signature"];
            string? timestamp = context.Request.Query["timestamp"];
            string? nonce     = context.Request.Query["nonce"];
            string? echostr   = context.Request.Query["echostr"];

            if (!WeChatChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning("寰俊 URL 楠岃瘉鏃堕棿鎴宠繃鏈?channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            if (!WeChatChannel.VerifySignature(settings.Token, timestamp, nonce, signature))
            {
                logger.LogWarning("寰俊 URL 楠岃瘉绛惧悕澶辫触 channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            logger.LogInformation("寰俊 URL 楠岃瘉鎴愬姛 channelId={ChannelId}", channelId);
            return Results.Text(echostr ?? string.Empty);
        })
        .WithTags("Webhooks");

        // POST锛氭秷鎭笌浜嬩欢鍥炶皟
        endpoints.MapPost("/channels/wechat/{channelId}/webhook", async (
            string channelId,
            HttpContext context,
            IChannelService channelService,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeChat");
            logger.LogInformation("鏀跺埌寰俊 Webhook 璇锋眰 channelId={ChannelId}", channelId);

            if (!channelService.TryGet(channelId, out IChannel? weChatChannel)
                || !weChatChannel.Config.IsEnabled
                || weChatChannel.Type != ChannelType.WeChat)
            {
                logger.LogWarning("寰俊娓犻亾鏈壘鍒版垨宸茬鐢?channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            WeChatChannelSettings settings = WeChatChannelSettings.TryParse(weChatChannel.Config.SettingJson) ?? new();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                logger.LogWarning("寰俊娓犻亾鏈厤缃?Token channelId={ChannelId}", channelId);
                return ApiErrors.BadRequest("Token is not configured.");
            }

            // 瀹夊叏妯″紡涓嬩娇鐢?msg_signature锛屾槑鏂囨ā寮忎娇鐢?signature
            string? msgSignature = context.Request.Query["msg_signature"];
            string? signature    = context.Request.Query["signature"];
            string? timestamp    = context.Request.Query["timestamp"];
            string? nonce        = context.Request.Query["nonce"];

            if (!WeChatChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning("寰俊 Webhook 鏃堕棿鎴宠繃鏈?channelId={ChannelId}", channelId);
                return Results.Json(new { success = false, message = "Timestamp expired or invalid" }, statusCode: 401);
            }

            using StreamReader reader = new(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            logger.LogDebug("寰俊 Webhook body: {Body}", body);

            if (!string.IsNullOrEmpty(msgSignature))
            {
                // 瀹夊叏妯″紡锛氫粠 XML 涓彁鍙?Encrypt 瀛楁
                string? msgEncrypt = ExtractXmlField(body, "Encrypt");
                if (!WeChatChannel.VerifySignature(settings.Token, timestamp, nonce, msgSignature, msgEncrypt))
                {
                    logger.LogWarning("寰俊 Webhook 绛惧悕楠岃瘉澶辫触锛堝畨鍏ㄦā寮忥級 channelId={ChannelId}", channelId);
                    return Results.Json(new { success = false, message = "Signature verification failed" }, statusCode: 401);
                }
            }
            else
            {
                // 鏄庢枃妯″紡锛氫粎涓夊瓧娈电鍚?
                if (!WeChatChannel.VerifySignature(settings.Token, timestamp, nonce, signature))
                {
                    logger.LogWarning("寰俊 Webhook 绛惧悕楠岃瘉澶辫触锛堟槑鏂囨ā寮忥級 channelId={ChannelId}", channelId);
                    return Results.Json(new { success = false, message = "Signature verification failed" }, statusCode: 401);
                }
            }

            string? response = await weChatChannel.HandleWebhookAsync(body, context.RequestAborted);
            return response is null ? Results.Ok() : Results.Content(response, "application/xml");
        })
        .WithTags("Webhooks");

        return endpoints;
    }

    /// <summary>浠?XML 瀛楃涓蹭腑鎻愬彇鎸囧畾鏍囩鍐呯殑鏂囨湰鍐呭锛堢畝鍗曞瓧绗︿覆瑙ｆ瀽锛屾棤闇€瀹屾暣 XML 瑙ｆ瀽鍣級銆?/summary>
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
    bool IsEnabled = true,
    string? Settings = null);

public sealed record ChannelUpdateRequest(
    string Id,
    string? DisplayName,
    string? ChannelType,
    bool IsEnabled = true,
    string? Settings = null);

public sealed record ChannelDeleteRequest(string Id);

public sealed record ChannelPublishRequest(string TargetId, string Content);
