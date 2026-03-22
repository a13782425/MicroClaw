using MicroClaw.Agent.Memory;
using MicroClaw.Channels;
using MicroClaw.Channels.Feishu;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Endpoints;

/// <summary>
/// F-C-6: 飞书文档导入为 DNA — 通过飞书文档 URL 或 Token 读取文档内容，
/// 写入指定层级（全局 / Agent / 会话）的 DNA 文件。
/// </summary>
public static class FeishuDocImportEndpoints
{
    public static IEndpointRouteBuilder MapFeishuDocImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── 全局 DNA 从飞书文档导入 ───────────────────────────────────────────

        endpoints.MapPost("/dna/import-from-feishu",
            async (FeishuDocImportRequest req, DNAService dna, ChannelConfigStore channelStore,
                   ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("FeishuDocImport");
                return await HandleImportAsync(
                    req, channelStore, logger,
                    writeAction: (safeName, safeCategory, content) =>
                        dna.WriteGlobal(safeCategory, safeName, content),
                    ct);
            })
        .WithTags("GlobalDNA");

        // ── Agent DNA 从飞书文档导入 ─────────────────────────────────────────

        endpoints.MapPost("/agents/{id}/dna/import-from-feishu",
            async (string id, FeishuDocImportRequest req, DNAService dna,
                   ChannelConfigStore channelStore, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("FeishuDocImport");
                return await HandleImportAsync(
                    req, channelStore, logger,
                    writeAction: (safeName, safeCategory, content) =>
                        dna.Write(id, safeCategory, safeName, content),
                    ct);
            })
        .WithTags("Agents");

        // ── 会话 DNA 从飞书文档导入 ──────────────────────────────────────────

        endpoints.MapPost("/sessions/{id}/dna/import-from-feishu",
            async (string id, FeishuDocImportRequest req, DNAService dna, SessionStore sessionStore,
                   ChannelConfigStore channelStore, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                if (sessionStore.Get(id) is null)
                    return Results.NotFound(new
                    {
                        success = false,
                        message = $"Session '{id}' not found.",
                        errorCode = "NOT_FOUND"
                    });

                var logger = loggerFactory.CreateLogger("FeishuDocImport");
                return await HandleImportAsync(
                    req, channelStore, logger,
                    writeAction: (safeName, safeCategory, content) =>
                        dna.WriteSession(id, safeCategory, safeName, content),
                    ct);
            })
        .WithTags("SessionDNA");

        return endpoints;
    }

    /// <summary>
    /// 公共处理逻辑：读取飞书文档内容并写入 DNA。
    /// </summary>
    private static async Task<IResult> HandleImportAsync(
        FeishuDocImportRequest req,
        ChannelConfigStore channelStore,
        ILogger logger,
        Func<string, string, string, GeneFile> writeAction,
        CancellationToken ct)
    {
        // 校验必填字段
        if (string.IsNullOrWhiteSpace(req.DocUrlOrToken))
            return Results.BadRequest(new { success = false, message = "DocUrlOrToken is required.", errorCode = "BAD_REQUEST" });

        if (string.IsNullOrWhiteSpace(req.FileName))
            return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });

        // 净化文件名和分类（防止路径穿越）
        string safeName = Path.GetFileName(req.FileName.Trim());
        if (string.IsNullOrWhiteSpace(safeName))
            return Results.BadRequest(new { success = false, message = "FileName is invalid.", errorCode = "BAD_REQUEST" });

        string safeCategory = SanitizeCategory(req.Category);

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

        // 读取飞书文档内容
        var (success, content, error) = await FeishuDocTools.ReadDocAsync(
            settings, req.DocUrlOrToken, logger, ct);

        if (!success || content is null)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = error ?? "读取飞书文档失败。",
                errorCode = "FEISHU_DOC_READ_FAILED"
            });
        }

        // 写入 DNA
        GeneFile file = writeAction(safeName, safeCategory, content);

        logger.LogInformation(
            "F-C-6 飞书文档导入 DNA 成功 fileName={FileName} charCount={CharCount}",
            safeName, content.Length);

        return Results.Ok(new
        {
            success = true,
            file,
            charCount = content.Length
        });
    }

    private static string SanitizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return string.Empty;
        return string.Join("/",
            category.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(Path.GetFileName)
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}

/// <summary>从飞书文档导入 DNA 的请求体。</summary>
public sealed record FeishuDocImportRequest(
    string DocUrlOrToken,
    string FileName,
    string? Category = null);
