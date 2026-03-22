using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-5: 飞书知识库（Wiki）搜索工具 — 提供 <c>search_feishu_wiki</c> AIFunction，
/// Agent 可按关键词搜索知识库节点（文章/文件夹），返回节点标题、Token 和类型。
/// 找到节点后，可配合 <c>read_feishu_doc</c> 工具进一步读取节点文档内容。
/// </summary>
public static class FeishuWikiTools
{
    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("search_feishu_wiki", "在飞书知识库（Wiki）中按关键词搜索节点（文章或文件夹），返回命中节点的标题、Token 和类型。可传入知识库 URL 或 Space ID 指定搜索范围；找到节点后可用 read_feishu_doc 读取具体内容。"),
    ];

    /// <summary>返回工具元数据（供工具列表 API 使用）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 根据已启用的飞书渠道配置创建知识库搜索工具实例。
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateTools(FeishuChannelSettings settings, ILogger logger)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("知识库 URL（如 https://xxxx.feishu.cn/wiki/xxxxxx）或知识库 Space ID（仅字母/数字/下划线/横线组成的字符串）")] string spaceUrlOrId,
                    [Description("搜索关键词，支持中文/英文/混合，最长 100 个字符")] string keyword,
                    [Description("单次返回的最大节点数，范围 1-50，默认 10")] int pageSize = 10,
                    CancellationToken ct = default) =>
                {
                    try
                    {
                        // 1. 解析 SpaceId
                        string spaceId = ExtractSpaceId(spaceUrlOrId);
                        if (string.IsNullOrWhiteSpace(spaceId))
                        {
                            return (object)new { success = false, error = "无法解析知识库 Space ID，请提供有效的飞书知识库 URL 或 Space ID。" };
                        }

                        // 2. 安全校验（防路径注入）
                        if (!IsValidSpaceId(spaceId))
                        {
                            return (object)new { success = false, error = "Space ID 格式不正确，只允许字母、数字、下划线和横线。" };
                        }

                        // 3. 关键词非空校验
                        if (string.IsNullOrWhiteSpace(keyword))
                        {
                            return (object)new { success = false, error = "搜索关键词不能为空。" };
                        }

                        // 4. 限制 pageSize 范围
                        int clampedPageSize = Math.Clamp(pageSize, 1, 50);

                        // 5. 获取 Tenant Access Token
                        string? tenantToken = await GetTenantAccessTokenAsync(settings, logger, ct);
                        if (string.IsNullOrWhiteSpace(tenantToken))
                        {
                            return (object)new { success = false, error = "获取飞书 Tenant Access Token 失败，请检查渠道 AppId/AppSecret 配置。" };
                        }

                        // 6. 搜索知识库节点
                        var nodes = await SearchWikiNodesAsync(
                            settings.ApiBaseUrl, spaceId, keyword, tenantToken, clampedPageSize, logger, ct);

                        if (nodes is null)
                        {
                            return (object)new { success = false, error = "搜索飞书知识库失败，请确认 Space ID 正确且机器人有访问权限。" };
                        }

                        logger.LogInformation(
                            "search_feishu_wiki 成功 spaceId={SpaceId} keyword={Keyword} hitCount={HitCount}",
                            spaceId, keyword, nodes.Count);
                        return (object)new
                        {
                            success = true,
                            spaceId,
                            keyword,
                            totalCount = nodes.Count,
                            nodes,
                            tip = nodes.Count > 0
                                ? "找到节点后可使用 read_feishu_doc 工具并传入节点的 docToken 读取详细内容。"
                                : "未找到匹配节点，请尝试其他关键词。"
                        };
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "search_feishu_wiki 执行失败");
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "search_feishu_wiki",
                description: "在飞书知识库（Wiki）中按关键词搜索节点（文章或文件夹），返回命中节点的标题、Token 和类型。可传入知识库 URL 或 Space ID 指定搜索范围；找到节点后可用 read_feishu_doc 读取具体内容。"),
        ];
    }

    /// <summary>
    /// 从飞书知识库 URL 中提取 Space ID，或直接返回输入（若非 HTTP URL）。
    /// <para>URL 格式示例：https://xxxx.feishu.cn/wiki/SpaceId 或 https://xxxx.feishu.cn/wiki/space/SpaceId</para>
    /// </summary>
    internal static string ExtractSpaceId(string input)
    {
        input = input.Trim();

        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
            {
                // 路径段数组，去除空段
                string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // 格式1: /wiki/{spaceId}  — 最后一段为 spaceId
                // 格式2: /wiki/space/{spaceId}  (部分私有部署) — 同样取最后一段
                // 两种格式均取路径最后一段
                string last = segments.LastOrDefault() ?? string.Empty;
                return last;
            }
            return string.Empty;
        }

        return input; // 直接当作 Space ID 使用
    }

    /// <summary>
    /// 验证 Space ID 格式：只允许字母、数字、下划线和横线，防止路径注入。
    /// </summary>
    internal static bool IsValidSpaceId(string spaceId)
    {
        if (string.IsNullOrWhiteSpace(spaceId)) return false;
        foreach (char c in spaceId)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                return false;
        }
        return true;
    }

    /// <summary>
    /// 调用飞书鉴权 API 获取 Tenant Access Token（与 FeishuDocTools 中逻辑一致，但每个工具类独立持有以避免跨类静态依赖）。
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
    /// 调用飞书 Wiki API 按关键词搜索知识库节点。
    /// GET /open-apis/wiki/v2/spaces/{space_id}/nodes/search
    /// </summary>
    private static async Task<List<WikiNodeResult>?> SearchWikiNodesAsync(
        string? apiBaseUrl, string spaceId, string keyword,
        string tenantToken, int pageSize, ILogger logger, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tenantToken);

        string baseUrl = (apiBaseUrl ?? "https://open.feishu.cn").TrimEnd('/');
        // spaceId 已通过 IsValidSpaceId 验证，仅含字母/数字/下划线/横线
        // keyword 仅用于 query 参数（URI 编码），无路径注入风险
        string encodedKeyword = Uri.EscapeDataString(keyword);
        string url = $"{baseUrl}/open-apis/wiki/v2/spaces/{spaceId}/nodes/search" +
                     $"?keyword={encodedKeyword}&page_size={pageSize}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
            {
                logger.LogWarning("飞书知识库搜索 API 返回错误 spaceId={SpaceId}: {Body}", spaceId, body);
                return null;
            }

            var results = new List<WikiNodeResult>();

            if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                dataEl.TryGetProperty("items", out var itemsEl) &&
                itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in itemsEl.EnumerateArray())
                {
                    string? title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                    string? nodeToken = item.TryGetProperty("node_token", out var nt) ? nt.GetString() : null;
                    string? objToken = item.TryGetProperty("obj_token", out var ot) ? ot.GetString() : null;
                    string? objType = item.TryGetProperty("obj_type", out var otp) ? otp.GetString() : null;
                    string? parentNodeToken = item.TryGetProperty("parent_node_token", out var pnt) ? pnt.GetString() : null;

                    results.Add(new WikiNodeResult(
                        Title: title ?? "(无标题)",
                        NodeToken: nodeToken ?? string.Empty,
                        DocToken: objToken ?? string.Empty,
                        ObjType: MapObjType(objType),
                        ParentNodeToken: parentNodeToken ?? string.Empty,
                        ReadHint: !string.IsNullOrEmpty(objToken)
                            ? $"可用 read_feishu_doc 工具并传入 docToken=\"{objToken}\" 读取此节点内容"
                            : "此节点类型暂不支持直接读取内容"
                    ));
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "飞书知识库搜索网络请求失败 spaceId={SpaceId}", spaceId);
            return null;
        }
    }

    /// <summary>
    /// 将飞书 obj_type 值映射为中文描述，提升 Agent 对节点类型的理解。
    /// </summary>
    private static string MapObjType(string? objType) => objType switch
    {
        "doc" => "飞书旧版文档",
        "docx" => "飞书新版文档",
        "sheet" => "电子表格",
        "bitable" => "多维表格",
        "mindnote" => "思维导图",
        "file" => "附件文件",
        "wiki" => "知识库节点",
        "slides" => "幻灯片",
        _ => objType ?? "未知类型"
    };

    /// <summary>知识库节点搜索结果。</summary>
    private sealed record WikiNodeResult(
        string Title,
        string NodeToken,
        string DocToken,
        string ObjType,
        string ParentNodeToken,
        string ReadHint);
}
