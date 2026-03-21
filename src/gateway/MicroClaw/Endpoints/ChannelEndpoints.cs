using System.Text.Json;
using MicroClaw.Channels;
using MicroClaw.Channels.Feishu;
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
                    return Results.Json(new { success = false, message = "Timestamp expired or invalid" }, statusCode: 401);
                }

                if (!FeishuChannel.VerifyWebhookSignature(timestamp, nonce, settings.EncryptKey, body, signature))
                {
                    logger.LogWarning("飞书 Webhook 签名验证失败 channelId={ChannelId}", channelId);
                    return Results.Json(new { success = false, message = "Signature verification failed" }, statusCode: 401);
                }
            }

            string? response = await feishuChannel.HandleWebhookAsync(body, config, context.RequestAborted);
            if (response is null)
                return Results.Ok();

            return Results.Content(response, "application/json");
        })
        .WithTags("Webhooks");

        return endpoints;
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
