using System.Text.Json;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Channels;
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
        endpoints.MapGet("/channels", (ChannelService store) =>
        {
            var result = store.All.Select(c => new
            {
                c.Id,
                c.DisplayName,
                ChannelType = ChannelService.SerializeChannelType(c.ChannelType),
                c.IsEnabled,
                Settings = ChannelService.MaskSettingsSecrets(c.SettingJson, c.ChannelType)
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

        endpoints.MapPost("/channels", (ChannelCreateRequest req, ChannelService store) =>
        {
            if (string.IsNullOrWhiteSpace(req.DisplayName))
                return ApiErrors.BadRequest("DisplayName is required.");

            ChannelEntity channel = new()
            {
                DisplayName = req.DisplayName.Trim(),
                ChannelType = ChannelService.ParseChannelType(req.ChannelType),
                IsEnabled = req.IsEnabled,
                SettingJson = req.Settings ?? "{}"
            };

            ChannelEntity created = store.Add(channel);
            return Results.Ok(new { created.Id });
        })
        .WithTags("Channels");

        endpoints.MapPost("/channels/update", (ChannelUpdateRequest req, ChannelService store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return ApiErrors.BadRequest("Id is required.");

            ChannelEntity incoming = new()
            {
                DisplayName = req.DisplayName?.Trim() ?? string.Empty,
                ChannelType = ChannelService.ParseChannelType(req.ChannelType),
                IsEnabled = req.IsEnabled,
                SettingJson = req.Settings ?? "{}"
            };

            ChannelEntity? updated = store.Update(req.Id, incoming);
            if (updated is null)
                return ApiErrors.NotFound($"Channel '{req.Id}' not found.");

            return Results.Ok(new { updated.Id });
        })
        .WithTags("Channels");

        endpoints.MapPost("/channels/delete", (ChannelDeleteRequest req, ChannelService store) =>
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


        // 渠道健康检查端点：委托给渠道实例的 GetDiagnosticsAsync，各渠道自行填充 Extra 字段
        endpoints.MapGet("/channels/{id}/health", async (
            string id,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            if (!channelService.TryGet(id, out IChannel? channel))
                return ApiErrors.NotFound($"Channel '{id}' not found.");

            ChannelDiagnostics diag = await channel.GetDiagnosticsAsync(ct);
            return Results.Ok(diag);
        })
        .WithTags("Channels");

        // 渠道统计端点：复用 GetDiagnosticsAsync，从 Extra 字段提取统计数据
        endpoints.MapGet("/channels/{id}/stats", async (
            string id,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            if (!channelService.TryGet(id, out IChannel? channel))
                return ApiErrors.NotFound($"Channel '{id}' not found.");

            ChannelDiagnostics diag = await channel.GetDiagnosticsAsync(ct);
            return Results.Ok(new
            {
                diag.ChannelId,
                diag.ChannelType,
                SignatureFailures = diag.Extra.TryGetValue("signatureFailures", out object? sf) ? sf : 0L,
                AiCallFailures    = diag.Extra.TryGetValue("aiCallFailures",    out object? af) ? af : 0L,
                ReplyFailures     = diag.Extra.TryGetValue("replyFailures",     out object? rf) ? rf : 0L,
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
            logger.LogInformation("收到飞书 Webhook 请求 channelId={ChannelId}", channelId);

            if (!channelService.TryGet(channelId, out IChannel? feishuChannel)
                || !feishuChannel.Config.IsEnabled
                || feishuChannel.Type != ChannelType.Feishu)
            {
                logger.LogWarning("飞书渠道未找到或已禁用 channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            using StreamReader reader = new(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            logger.LogDebug("飞书 Webhook body: {Body}", body);

            // 将请求头传给渠道实例，签名验证由渠道内部负责
            Dictionary<string, string?> headers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Lark-Signature"]          = context.Request.Headers["X-Lark-Signature"],
                ["X-Lark-Request-Timestamp"]  = context.Request.Headers["X-Lark-Request-Timestamp"],
                ["X-Lark-Request-Nonce"]      = context.Request.Headers["X-Lark-Request-Nonce"],
            };

            WebhookResult result = await feishuChannel.HandleWebhookAsync(body, headers, context.RequestAborted);
            if (result.StatusCode != 200)
                return Results.Json(new { success = false, message = result.Body }, statusCode: result.StatusCode);
            if (result.Body is null)
                return Results.Ok();
            return Results.Content(result.Body, "application/json");
        })
        .WithTags("Webhooks");

       
        endpoints.MapGet("/channels/wecom/{channelId}/webhook", (
            string channelId,
            HttpContext context,
            ChannelService store,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeCom");

            ChannelEntity? config = store.GetById(channelId);
            if (config is null || !config.IsEnabled || config.ChannelType != ChannelType.WeCom)
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.NotFound();
            }

            WeComChannelSettings settings = WeComChannelSettings.TryParse(config.SettingJson) ?? new();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.BadRequest();
            }

            string? msgSignature = context.Request.Query["msg_signature"];
            string? timestamp    = context.Request.Query["timestamp"];
            string? nonce        = context.Request.Query["nonce"];
            string? echostr      = context.Request.Query["echostr"];

            if (!WeComChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            // URL 楠岃瘉闃舵鏃?msg_encrypt锛屼娇鐢ㄥ熀纭€涓夊瓧娈电鍚?
            if (!WeComChannel.VerifySignature(settings.Token, timestamp, nonce, msgSignature))
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            logger.LogInformation(" channelId={ChannelId}", channelId);
            return Results.Text(echostr ?? string.Empty);
        })
        .WithTags("Webhooks");

        // POST 
        endpoints.MapPost("/channels/wecom/{channelId}/webhook", async (
            string channelId,
            HttpContext context,
            IChannelService channelService,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeCom");
            logger.LogInformation("  channelId={ChannelId}", channelId);

            if (!channelService.TryGet(channelId, out IChannel? weComChannel)
                || !weComChannel.Config.IsEnabled
                || weComChannel.Type != ChannelType.WeCom)
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            if (string.IsNullOrWhiteSpace(WeComChannelSettings.TryParse(weComChannel.Config.SettingJson)?.Token))
            {
                logger.LogWarning(" Token channelId={ChannelId}", channelId);
                return ApiErrors.BadRequest("Token is not configured.");
            }

            WeComChannelSettings settings = WeComChannelSettings.TryParse(weComChannel.Config.SettingJson)!;

            string? msgSignature = context.Request.Query["msg_signature"];
            string? timestamp    = context.Request.Query["timestamp"];
            string? nonce        = context.Request.Query["nonce"];

            if (!WeComChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.Json(new { success = false, message = "Timestamp expired or invalid" }, statusCode: 401);
            }

            using StreamReader reader = new(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            logger.LogDebug(" Webhook body: {Body}", body);

            //  
            string? msgEncrypt = ExtractXmlField(body, "Encrypt");

            if (!WeComChannel.VerifySignature(settings.Token, timestamp, nonce, msgSignature, msgEncrypt))
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.Json(new { success = false, message = "Signature verification failed" }, statusCode: 401);
            }

            WebhookResult weComResult = await weComChannel.HandleWebhookAsync(body, cancellationToken: context.RequestAborted);
            if (weComResult.StatusCode != 200)
                return Results.Json(new { success = false, message = weComResult.Body }, statusCode: weComResult.StatusCode);
            return weComResult.Body is null ? Results.Ok() : Results.Content(weComResult.Body, "application/xml");
        })
        .WithTags("Webhooks");

        // 

        // GET 
        endpoints.MapGet("/channels/wechat/{channelId}/webhook", (
            string channelId,
            HttpContext context,
            ChannelService store,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeChat");

            ChannelEntity? config = store.GetById(channelId);
            if (config is null || !config.IsEnabled || config.ChannelType != ChannelType.WeChat)
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.NotFound();
            }

            WeChatChannelSettings settings = WeChatChannelSettings.TryParse(config.SettingJson) ?? new();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.BadRequest();
            }

            string? signature = context.Request.Query["signature"];
            string? timestamp = context.Request.Query["timestamp"];
            string? nonce     = context.Request.Query["nonce"];
            string? echostr   = context.Request.Query["echostr"];

            if (!WeChatChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            if (!WeChatChannel.VerifySignature(settings.Token, timestamp, nonce, signature))
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.Unauthorized();
            }

            logger.LogInformation(" channelId={ChannelId}", channelId);
            return Results.Text(echostr ?? string.Empty);
        })
        .WithTags("Webhooks");

        // POST 
        endpoints.MapPost("/channels/wechat/{channelId}/webhook", async (
            string channelId,
            HttpContext context,
            IChannelService channelService,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook.WeChat");
            logger.LogInformation("  channelId={ChannelId}", channelId);

            if (!channelService.TryGet(channelId, out IChannel? weChatChannel)
                || !weChatChannel.Config.IsEnabled
                || weChatChannel.Type != ChannelType.WeChat)
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            WeChatChannelSettings settings = WeChatChannelSettings.TryParse(weChatChannel.Config.SettingJson) ?? new();
            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                logger.LogWarning(" Token channelId={ChannelId}", channelId);
                return ApiErrors.BadRequest("Token is not configured.");
            }

            //  
            string? msgSignature = context.Request.Query["msg_signature"];
            string? signature    = context.Request.Query["signature"];
            string? timestamp    = context.Request.Query["timestamp"];
            string? nonce        = context.Request.Query["nonce"];

            if (!WeChatChannel.IsTimestampFresh(timestamp, settings.WebhookTimestampToleranceSeconds))
            {
                logger.LogWarning(" channelId={ChannelId}", channelId);
                return Results.Json(new { success = false, message = "Timestamp expired or invalid" }, statusCode: 401);
            }

            using StreamReader reader = new(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            logger.LogDebug("  Webhook body: {Body}", body);

            if (!string.IsNullOrEmpty(msgSignature))
            {
                //  
                string? msgEncrypt = ExtractXmlField(body, "Encrypt");
                if (!WeChatChannel.VerifySignature(settings.Token, timestamp, nonce, msgSignature, msgEncrypt))
                {
                    logger.LogWarning(" channelId={ChannelId}", channelId);
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

            WebhookResult weChatResult = await weChatChannel.HandleWebhookAsync(body, cancellationToken: context.RequestAborted);
            if (weChatResult.StatusCode != 200)
                return Results.Json(new { success = false, message = weChatResult.Body }, statusCode: weChatResult.StatusCode);
            return weChatResult.Body is null ? Results.Ok() : Results.Content(weChatResult.Body, "application/xml");
        })
        .WithTags("Webhooks");

        return endpoints;
    }

    /// <summary> </summary>
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
