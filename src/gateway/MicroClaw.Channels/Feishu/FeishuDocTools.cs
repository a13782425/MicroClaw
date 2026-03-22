using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-1: 飞书文档读取工具 — 提供 <c>read_feishu_doc</c> AIFunction，
/// Agent 可通过文档 URL 或 Token 读取飞书新版文档（docx 格式）的纯文本内容。
/// </summary>
public static class FeishuDocTools
{
    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("read_feishu_doc", "通过飞书文档 URL 或文档 Token 读取飞书文档的纯文本内容（支持飞书新版 docx 格式）。文档必须对机器人应用可见（已分享或在可访问空间内）。"),
        ("write_feishu_doc", "向飞书文档末尾追加内容（支持纯文本段落或代码块）。文档必须对机器人应用有编辑权限。"),
    ];

    /// <summary>返回工具元数据（供工具列表 API 使用）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 根据已启用的飞书渠道配置创建文档读取工具实例。
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateTools(FeishuChannelSettings settings, ILogger logger)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("飞书文档 URL（如 https://xxxx.feishu.cn/docx/xxxxxx）或文档 Token（仅字母/数字/下划线/横线组成的字符串）")] string docUrlOrToken,
                    CancellationToken ct) =>
                {
                    try
                    {
                        string docToken = ExtractDocToken(docUrlOrToken);
                        if (string.IsNullOrWhiteSpace(docToken))
                        {
                            return (object)new { success = false, error = "无法解析文档 Token，请提供有效的飞书文档 URL 或 Token。" };
                        }

                        // 校验 token 格式，防止路径注入
                        if (!IsValidDocToken(docToken))
                        {
                            return (object)new { success = false, error = "文档 Token 格式不正确，只允许字母、数字、下划线和横线。" };
                        }

                        string? tenantToken = await GetTenantAccessTokenAsync(settings, logger, ct);
                        if (string.IsNullOrWhiteSpace(tenantToken))
                        {
                            return (object)new { success = false, error = "获取飞书 Tenant Access Token 失败，请检查渠道 AppId/AppSecret 配置。" };
                        }

                        string? content = await ReadDocumentContentAsync(
                            settings.ApiBaseUrl, docToken, tenantToken, logger, ct);

                        if (content is null)
                        {
                            return (object)new { success = false, error = "读取飞书文档内容失败，请确认文档 Token 正确且机器人有访问权限。" };
                        }

                        int charCount = content.Length;
                        logger.LogInformation("read_feishu_doc 成功 docToken={DocToken} charCount={CharCount}", docToken, charCount);
                        return (object)new { success = true, docToken, content, charCount };
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "read_feishu_doc 执行失败");
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "read_feishu_doc",
                description: "通过飞书文档 URL 或文档 Token 读取飞书文档的纯文本内容（支持飞书新版 docx 格式）。文档必须对机器人应用可见（已分享或在可访问空间内）。"),
            AIFunctionFactory.Create(
                async (
                    [Description("飞书文档 URL（如 https://xxxx.feishu.cn/docx/xxxxxx）或文档 Token（仅字母/数字/下划线/横线组成的字符串）")] string docUrlOrToken,
                    [Description("要追加的内容文本")] string content,
                    [Description("内容类型：'text'（普通文本段落，默认）或 'code'（代码块）")] string contentType = "text",
                    [Description("代码语言（仅在 contentType 为 'code' 时有效，支持 python/javascript/typescript/java/csharp/go/rust/sql/shell/yaml/json 等，默认 plaintext）")] string language = "plaintext",
                    CancellationToken ct = default) =>
                {
                    try
                    {
                        string docToken = ExtractDocToken(docUrlOrToken);
                        if (string.IsNullOrWhiteSpace(docToken))
                        {
                            return (object)new { success = false, error = "无法解析文档 Token，请提供有效的飞书文档 URL 或 Token。" };
                        }

                        if (!IsValidDocToken(docToken))
                        {
                            return (object)new { success = false, error = "文档 Token 格式不正确，只允许字母、数字、下划线和横线。" };
                        }

                        if (string.IsNullOrEmpty(content))
                        {
                            return (object)new { success = false, error = "追加内容不能为空。" };
                        }

                        string? tenantToken = await GetTenantAccessTokenAsync(settings, logger, ct);
                        if (string.IsNullOrWhiteSpace(tenantToken))
                        {
                            return (object)new { success = false, error = "获取飞书 Tenant Access Token 失败，请检查渠道 AppId/AppSecret 配置。" };
                        }

                        bool wrote = await AppendBlockToDocAsync(
                            settings.ApiBaseUrl, docToken, tenantToken, content, contentType, language, logger, ct);
                        if (!wrote)
                        {
                            return (object)new { success = false, error = "追加内容到飞书文档失败，请确认文档 Token 正确且机器人有编辑权限。" };
                        }

                        logger.LogInformation(
                            "write_feishu_doc 成功 docToken={DocToken} contentType={ContentType} charCount={CharCount}",
                            docToken, contentType, content.Length);
                        return (object)new { success = true, docToken, contentType, charCount = content.Length };
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "write_feishu_doc 执行失败");
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "write_feishu_doc",
                description: "向飞书文档末尾追加内容（支持纯文本段落或代码块）。文档必须对机器人应用有编辑权限。"),
        ];
    }

    /// <summary>
    /// 从飞书文档 URL 中提取文档 Token（路径最后一段），或直接返回输入（若非 HTTP URL）。
    /// </summary>
    internal static string ExtractDocToken(string input)
    {
        input = input.Trim();

        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
            {
                // 取路径最后一段（去除查询参数、片段和末尾斜杠）
                string lastSegment = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
                return lastSegment;
            }
            return string.Empty;
        }

        return input; // 直接当作 token 使用
    }

    /// <summary>
    /// 验证文档 Token 格式：只允许字母、数字、下划线和横线，防止路径注入。
    /// </summary>
    internal static bool IsValidDocToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        foreach (char c in token)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                return false;
        }
        return true;
    }

    /// <summary>
    /// 调用飞书鉴权 API 获取 Tenant Access Token。
    /// </summary>
    private static async Task<string?> GetTenantAccessTokenAsync(
        FeishuChannelSettings settings, ILogger logger, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        string baseUrl = (settings.ApiBaseUrl ?? "https://open.feishu.cn").TrimEnd('/');
        string url = $"{baseUrl}/open-apis/auth/v3/tenant_access_token/internal";

        string payload = JsonSerializer.Serialize(new
        {
            app_id = settings.AppId,
            app_secret = settings.AppSecret
        });
        using var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.PostAsync(url, httpContent, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() == 0 &&
                doc.RootElement.TryGetProperty("tenant_access_token", out var tokenEl))
            {
                return tokenEl.GetString();
            }

            logger.LogWarning("获取飞书 Tenant Access Token 失败，响应: {Body}", body);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取飞书 Tenant Access Token 网络请求失败");
            return null;
        }
    }

    /// <summary>
    /// 调用飞书 docx API 读取文档纯文本内容。
    /// GET /open-apis/docx/v1/documents/{document_id}/raw_content
    /// </summary>
    private static async Task<string?> ReadDocumentContentAsync(
        string? apiBaseUrl, string docToken, string tenantToken, ILogger logger, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tenantToken);

        string baseUrl = (apiBaseUrl ?? "https://open.feishu.cn").TrimEnd('/');
        // docToken 已通过 IsValidDocToken 验证，仅含字母/数字/下划线/横线
        string url = $"{baseUrl}/open-apis/docx/v1/documents/{docToken}/raw_content";

        try
        {
            using var response = await client.GetAsync(url, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
            {
                logger.LogWarning("飞书文档读取 API 返回错误 docToken={DocToken}: {Body}", docToken, body);
                return null;
            }

            if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                dataEl.TryGetProperty("content", out var contentEl))
            {
                return contentEl.GetString();
            }

            logger.LogWarning("飞书文档读取响应格式异常 docToken={DocToken}: {Body}", docToken, body);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "飞书文档读取网络请求失败 docToken={DocToken}", docToken);
            return null;
        }
    }

    /// <summary>
    /// 调用飞书 docx API 向文档末尾追加块内容。
    /// POST /open-apis/docx/v1/documents/{document_id}/blocks/{block_id}/children
    /// 说明：飞书新版文档（docx）中，文档 ID 与根 Block ID 一致；不传 index 字段时默认追加到末尾。
    /// </summary>
    private static async Task<bool> AppendBlockToDocAsync(
        string? apiBaseUrl, string docToken, string tenantToken,
        string content, string contentType, string language,
        ILogger logger, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tenantToken);

        string baseUrl = (apiBaseUrl ?? "https://open.feishu.cn").TrimEnd('/');
        // docToken 已通过 IsValidDocToken 验证，仅含字母/数字/下划线/横线
        string url = $"{baseUrl}/open-apis/docx/v1/documents/{docToken}/blocks/{docToken}/children";

        object block = contentType.Equals("code", StringComparison.OrdinalIgnoreCase)
            ? BuildCodeBlock(content, language)
            : BuildTextBlock(content);

        string payload = JsonSerializer.Serialize(new { children = new[] { block } });
        using var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.PostAsync(url, httpContent, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            using var respDoc = JsonDocument.Parse(body);

            if (respDoc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() == 0)
                return true;

            logger.LogWarning("飞书文档写入 API 返回错误 docToken={DocToken}: {Body}", docToken, body);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "飞书文档写入网络请求失败 docToken={DocToken}", docToken);
            return false;
        }
    }

    /// <summary>构建飞书文档纯文本段落块（block_type = 2）。</summary>
    private static object BuildTextBlock(string content) => new
    {
        block_type = 2,
        text = new
        {
            elements = new[]
            {
                new { text_run = new { content, text_element_style = new { } } }
            },
            style = new { }
        }
    };

    /// <summary>构建飞书文档代码块（block_type = 14），自动映射语言编码。</summary>
    private static object BuildCodeBlock(string content, string language) => new
    {
        block_type = 14,
        code = new
        {
            elements = new[]
            {
                new { text_run = new { content, text_element_style = new { } } }
            },
            style = new { language = GetLanguageCode(language), wrap = false }
        }
    };

    /// <summary>
    /// 将语言字符串映射为飞书文档代码块的语言编号。
    /// 参考飞书开放平台 CodeLanguage 枚举定义。
    /// </summary>
    private static int GetLanguageCode(string language) => language.ToLowerInvariant() switch
    {
        "bash" => 7,
        "c#" or "csharp" => 9,
        "c++" or "cpp" => 10,
        "c" => 11,
        "go" or "golang" => 22,
        "html" => 24,
        "json" => 28,
        "java" => 29,
        "javascript" or "js" => 30,
        "kotlin" => 32,
        "lua" => 36,
        "makefile" => 38,
        "markdown" or "md" => 39,
        "php" => 44,
        "powershell" or "ps" => 46,
        "python" or "py" => 49,
        "ruby" or "rb" => 52,
        "rust" or "rs" => 53,
        "sql" => 56,
        "shell" or "sh" => 58,
        "swift" => 59,
        "typescript" or "ts" => 61,
        "xml" => 64,
        "yaml" or "yml" => 65,
        _ => 1 // PlainText
    };
}
