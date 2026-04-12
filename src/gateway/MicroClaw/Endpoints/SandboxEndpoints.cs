using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration;
using MicroClaw.Sessions;
using MicroClaw.Services;

namespace MicroClaw.Endpoints;

public static class SandboxEndpoints
{
    /// <summary>受保护端点（需要 JWT）：列出沙盒文件树、生成下载 Token。</summary>
    public static IEndpointRouteBuilder MapSandboxProtectedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/sessions/{id}/sandbox — 递归列出会话沙盒文件树
        endpoints.MapGet("/sessions/{id}/sandbox", (
            string id,
            ISessionService store,
            SandboxTokenService tokenSvc) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            string sandboxDir = GetSandboxDir(id);
            if (!Directory.Exists(sandboxDir))
                return Results.Ok(Array.Empty<SandboxNode>());

            var nodes = BuildTree(sandboxDir, sandboxDir, tokenSvc, id);
            return Results.Ok(nodes);
        })
        .WithTags("Sandbox");

        // POST /api/sessions/{id}/sandbox/token — 为指定文件生成短期匿名下载 Token
        endpoints.MapPost("/sessions/{id}/sandbox/token", (
            string id,
            SandboxTokenRequest req,
            ISessionService store,
            SandboxTokenService tokenSvc) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            if (string.IsNullOrWhiteSpace(req.RelativePath))
                return Results.BadRequest(new { success = false, message = "RelativePath is required.", errorCode = "BAD_REQUEST" });

            string sandboxDir = GetSandboxDir(id);
            string safeRelative = req.RelativePath.Replace('\\', '/').TrimStart('/');

            // 路径穿越防护
            string target = Path.GetFullPath(Path.Combine(sandboxDir, safeRelative));
            string root = Path.GetFullPath(sandboxDir);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { success = false, message = "Invalid path.", errorCode = "BAD_REQUEST" });

            if (!File.Exists(target))
                return Results.NotFound(new { success = false, message = $"File '{safeRelative}' not found in sandbox.", errorCode = "NOT_FOUND" });

            int expiryMinutes = MicroClawConfig.Get<SandboxOptions>().TokenExpiryMinutes;
            string downloadUrl = tokenSvc.GenerateDownloadUrl(id, safeRelative);

            return Results.Ok(new
            {
                downloadUrl,
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes),
            });
        })
        .WithTags("Sandbox");

        return endpoints;
    }

    /// <summary>公开端点（无需 JWT）：凭 Token 下载沙盒文件。</summary>
    public static IEndpointRouteBuilder MapSandboxPublicEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/sandbox/download?token=... — 无需认证
        endpoints.MapGet("/sandbox/download", (string token, SandboxTokenService tokenSvc, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(token))
                return Results.BadRequest(new { success = false, message = "Token is required.", errorCode = "BAD_REQUEST" });

            var result = tokenSvc.ValidateToken(token);
            if (result is null)
                return Results.Json(
                    new { success = false, message = "Invalid or expired token.", errorCode = "UNAUTHORIZED" },
                    statusCode: 401);

            var (sessionId, relativePath) = result.Value;
            string sandboxDir = GetSandboxDir(sessionId);

            string safeRelative = relativePath.Replace('\\', '/').TrimStart('/');
            string filePath = Path.GetFullPath(Path.Combine(sandboxDir, safeRelative));
            string root = Path.GetFullPath(sandboxDir);

            // 路径穿越防护（二次验证，防止 Token 生成后沙盒目录被修改的极端情况）
            if (!filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { success = false, message = "Invalid path.", errorCode = "BAD_REQUEST" });

            if (!File.Exists(filePath))
                return Results.NotFound(new { success = false, message = "File not found.", errorCode = "NOT_FOUND" });

            string fileName = Path.GetFileName(filePath);
            string mimeType = GetMimeType(fileName);

            ctx.Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{Uri.EscapeDataString(fileName)}\"");

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            return Results.Stream(stream, contentType: mimeType, enableRangeProcessing: true);
        })
        .AllowAnonymous()
        .WithTags("Sandbox");

        return endpoints;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string GetSandboxDir(string sessionId)
    {
        string sessionsDir = MicroClawConfig.Env.SessionsDir;
        return Path.Combine(sessionsDir, sessionId, "sandbox");
    }

    private static List<SandboxNode> BuildTree(string rootDir, string currentDir, SandboxTokenService tokenSvc, string sessionId)
    {
        var nodes = new List<SandboxNode>();

        foreach (string dir in Directory.GetDirectories(currentDir).OrderBy(d => d))
        {
            string name = Path.GetFileName(dir);
            string relPath = Path.GetRelativePath(rootDir, dir).Replace('\\', '/');
            var children = BuildTree(rootDir, dir, tokenSvc, sessionId);
            nodes.Add(new SandboxNode(name, relPath, 0, DateTimeOffset.MinValue, IsDirectory: true, children));
        }

        foreach (string file in Directory.GetFiles(currentDir).OrderBy(f => f))
        {
            string name = Path.GetFileName(file);
            string relPath = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
            var fi = new FileInfo(file);
            string downloadUrl = tokenSvc.GenerateDownloadUrl(sessionId, relPath);
            nodes.Add(new SandboxNode(name, relPath, fi.Length, fi.LastWriteTimeUtc, IsDirectory: false, null, downloadUrl));
        }

        return nodes;
    }

    private static string GetMimeType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".md"  => "text/markdown",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".html" or ".htm" => "text/html",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".zip" => "application/zip",
            _ => "application/octet-stream",
        };
    }
}

// ── Request / Response records ───────────────────────────────────────────────

/// <summary>沙盒文件树节点。</summary>
public sealed record SandboxNode(
    string Name,
    string RelativePath,
    long Size,
    DateTimeOffset ModifiedAt,
    bool IsDirectory,
    IReadOnlyList<SandboxNode>? Children,
    string? DownloadUrl = null);

/// <summary>生成下载 Token 的请求体。</summary>
public sealed record SandboxTokenRequest(string RelativePath);
