using MicroClaw.Agent.Memory;
using MicroClaw.Channels;
using MicroClaw.Channels.Feishu;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Endpoints;

/// <summary>
/// F-C-6: 飞书文档导入 Session DNA — 读取飞书文档内容并写入 Session 固定 DNA 文件（SOUL/USER/AGENTS）。
/// </summary>
public static class FeishuDocImportEndpoints
{
    public static IEndpointRouteBuilder MapFeishuDocImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── 会话 DNA 从飞书文档导入 ──────────────────────────────────────────

        endpoints.MapPost("/sessions/{id}/dna/import-from-feishu",
            async (string id, FeishuDocImportRequest req, SessionDnaService sessionDna,
                   SessionStore sessionStore, ChannelConfigStore channelStore,
                   ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                if (sessionStore.Get(id) is null)
                    return Results.NotFound(new
                    {
                        success = false,
                        message = $"Session '{id}' not found.",
                        errorCode = "NOT_FOUND"
                    });

                // 校验 FileName 必须是三个固定文件之一
                if (string.IsNullOrWhiteSpace(req.FileName))
                    return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });

                string fileName = req.FileName.Trim();
                if (!SessionDnaService.IsAllowedFileName(fileName))
                    return Results.BadRequest(new
                    {
                        success = false,
                        message = $"FileName must be one of: {string.Join(", ", SessionDnaService.FixedFileNames)}",
                        errorCode = "INVALID_FILE_NAME"
                    });

                var logger = loggerFactory.CreateLogger("FeishuDocImport");
                return await HandleImportAsync(id, fileName, req, channelStore, sessionDna, logger, ct);
            })
        .WithTags("SessionDNA");

        return endpoints;
    }

    private static async Task<IResult> HandleImportAsync(
        string sessionId,
        string fileName,
        FeishuDocImportRequest req,
        ChannelConfigStore channelStore,
        SessionDnaService sessionDna,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DocUrlOrToken))
            return Results.BadRequest(new { success = false, message = "DocUrlOrToken is required.", errorCode = "BAD_REQUEST" });

        // 获取第一个已启用的飞书渠道配置
        ChannelConfig? feishuConfig = channelStore
            .GetByType(ChannelType.Feishu)
            .FirstOrDefault(c => c.IsEnabled);

        if (feishuConfig is null)
            return Results.Problem(
                title: "飞书渠道未配置",
                detail: "未找到已启用的飞书渠道，请先在渠道管理中添加并启用飞书渠道配置。",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(feishuConfig.SettingsJson) ?? new();

        if (string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
            return Results.Problem(
                title: "飞书渠道密钥缺失",
                detail: "飞书渠道配置中 AppId 或 AppSecret 为空，请完善配置后重试。",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        var (success, content, error) = await FeishuDocTools.ReadDocAsync(
            settings, req.DocUrlOrToken, logger, ct);

        if (!success || content is null)
            return Results.BadRequest(new
            {
                success = false,
                message = error ?? "读取飞书文档失败。",
                errorCode = "FEISHU_DOC_READ_FAILED"
            });

        SessionDnaFileInfo? updated = sessionDna.Update(sessionId, fileName, content);
        if (updated is null)
            return Results.BadRequest(new { success = false, message = "写入 DNA 文件失败。", errorCode = "DNA_WRITE_FAILED" });

        logger.LogInformation(
            "F-C-6 飞书文档导入 Session DNA 成功 sessionId={SessionId} fileName={FileName} charCount={CharCount}",
            sessionId, fileName, content.Length);

        return Results.Ok(new { success = true, file = updated, charCount = content.Length });
    }
}

/// <summary>从飞书文档导入 Session DNA 的请求体。</summary>
public sealed record FeishuDocImportRequest(
    string DocUrlOrToken,
    string FileName);

