using System.Text.Json;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Channels;
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
        // GET：URL 验证回调（企业微信 / 微信公众号 echostr 验证）
        // 端点层仅收集请求参数，签名验证由各渠道实现负责
        endpoints.MapGet("/channels/{channelId}/webhook", async (
            string channelId,
            HttpContext context,
            IChannelService channelService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook");
            logger.LogInformation("收到 Webhook URL 验证请求 channelId={ChannelId}", channelId);

            if (!channelService.TryGet(channelId, out IChannel? channel) || !channel.Config.IsEnabled)
            {
                logger.LogWarning("渠道未找到或已禁用 channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            Dictionary<string, string?> headers = BuildHeaders(context);
            WebhookResult result = await channel.HandleWebhookAsync(string.Empty, headers, ct);
            return ToHttpResult(result);
        })
        .WithTags("Webhooks");

        // POST：消息接收（所有渠道统一入口）
        // 签名验证、消息解析全部由渠道实现负责，端点层保持渠道无关
        endpoints.MapPost("/channels/{channelId}/webhook", async (
            string channelId,
            HttpContext context,
            IChannelService channelService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            ILogger logger = loggerFactory.CreateLogger("ChannelWebhook");
            logger.LogInformation("收到 Webhook 消息请求 channelId={ChannelId}", channelId);

            if (!channelService.TryGet(channelId, out IChannel? channel) || !channel.Config.IsEnabled)
            {
                logger.LogWarning("渠道未找到或已禁用 channelId={ChannelId}", channelId);
                return ApiErrors.NotFound("Channel not found or disabled.");
            }

            using StreamReader reader = new(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            logger.LogDebug("Webhook body: {Body}", body);

            Dictionary<string, string?> headers = BuildHeaders(context);
            // 预解析 XML Encrypt 字段注入字典，避免渠道内重复解析请求体
            if (!headers.ContainsKey("encrypt"))
            {
                string? encrypt = ExtractXmlField(body, "Encrypt");
                if (encrypt is not null)
                    headers["encrypt"] = encrypt;
            }

            WebhookResult result = await channel.HandleWebhookAsync(body, headers, ct);
            return ToHttpResult(result);
        })
        .WithTags("Webhooks");

        return endpoints;
    }

    /// <summary>将 HTTP 请求头和查询参数合并到统一字典，供渠道实现读取。</summary>
    private static Dictionary<string, string?> BuildHeaders(HttpContext context)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in context.Request.Headers)
            dict[key] = value.ToString();
        // 查询参数后写，覆盖同名请求头（避免 Content-Type 等头被污染）
        foreach (var (key, value) in context.Request.Query)
            dict[key] = value.ToString();
        return dict;
    }

    /// <summary>根据渠道返回的 ContentType 构造对应的 HTTP 响应。</summary>
    private static IResult ToHttpResult(WebhookResult result)
    {
        if (result.StatusCode != 200)
            return Results.Json(new { success = false, message = result.Body }, statusCode: result.StatusCode);
        if (result.Body is null)
            return Results.Ok();
        string contentType = result.ContentType ?? "application/json";
        return Results.Content(result.Body, contentType);
    }

    /// <summary>从 XML 字符串中提取指定标签的文本内容（不依赖 XML 解析器）。</summary>
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
