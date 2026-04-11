using System.ComponentModel;
using FeishuNetSdk;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-5: 飞书知识库（Wiki）搜索工具 — 提供 <c>search_feishu_wiki</c> AIFunction，
/// Agent 可按关键词搜索知识库节点（文章/文件夹），返回节点标题、Token 和类型。
/// 找到节点后，可配合 <c>read_feishu_doc</c> 工具进一步读取节点文档内容。
/// <para>
/// 注意：FeishuNetSdk 未提供 wiki 节点搜索 API，此实现使用节点列表 + 客户端关键词过滤，
/// 仅检索指定层级（根节点或指定父节点下的子节点），不等同于全库深度搜索。
/// </para>
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
    public static IReadOnlyList<AIFunction> CreateTools(FeishuChannelSettings settings, IFeishuTenantApi api, ILogger logger)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("知识库 URL（如 https://xxxx.feishu.cn/wiki/xxxxxx）或知识库 Space ID（仅字母/数字/下划线/横线组成的字符串）")] string spaceUrlOrId,
                    [Description("搜索关键词，支持中文/英文/混合，最长 100 个字符")] string keyword,
                    [Description("父节点 Token（可选），不传时搜索知识库根节点下的子节点")] string parentNodeToken = "",
                    [Description("单次返回的最大节点数，范围 1-50，默认 10")] int pageSize = 10,
                    CancellationToken ct = default) =>
                {
                    try
                    {
                        string spaceId = ExtractSpaceId(spaceUrlOrId);
                        if (string.IsNullOrWhiteSpace(spaceId))
                            return (object)new { success = false, error = "无法解析知识库 Space ID，请提供有效的飞书知识库 URL 或 Space ID。" };

                        if (!IsValidSpaceId(spaceId))
                            return (object)new { success = false, error = "Space ID 格式不正确，只允许字母、数字、下划线和横线。" };

                        // F-G-3: Whitelist check
                        if (settings.AllowedWikiSpaceIds.Length > 0 &&
                            !settings.AllowedWikiSpaceIds.Contains(spaceId, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该知识库 Space ID 不在渠道允许的白名单内，Agent 无权搜索此知识库。" };
                        }

                        if (string.IsNullOrWhiteSpace(keyword))
                            return (object)new { success = false, error = "搜索关键词不能为空。" };

                        int clampedPageSize = Math.Clamp(pageSize, 1, 50);

                        // SDK node listing — paginate up to 200 nodes max to client-side filter
                        string? parent = string.IsNullOrWhiteSpace(parentNodeToken) ? null : parentNodeToken.Trim();
                        var matchedNodes = new List<object>();
                        string? pageToken = null;
                        int maxPages = 4; // 50 * 4 = 200 nodes max scanned

                        for (int page = 0; page < maxPages && matchedNodes.Count < clampedPageSize; page++)
                        {
                            var response = await api.GetWikiV2SpacesBySpaceIdNodesAsync(
                                spaceId, page_size: 50, page_token: pageToken, parent_node_token: parent);

                            if (response.Code != 0)
                            {
                                logger.LogWarning("飞书知识库节点列表 API 返回错误 spaceId={SpaceId}: {Msg}", spaceId, response.Msg);
                                return (object)new { success = false, error = response.Msg ?? "查询飞书知识库失败，请确认 Space ID 正确且机器人有访问权限。" };
                            }

                            var items = response.Data?.Items;
                            if (items is null || items.Length == 0) break;

                            foreach (var node in items)
                            {
                                if (matchedNodes.Count >= clampedPageSize) break;

                                if (node.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    matchedNodes.Add(new
                                    {
                                        title = node.Title,
                                        nodeToken = node.NodeToken,
                                        docToken = node.ObjToken ?? string.Empty,
                                        objType = MapObjType(node.ObjType),
                                        parentNodeToken = node.ParentNodeToken ?? string.Empty,
                                        hasChild = node.HasChild ?? false,
                                        readHint = !string.IsNullOrEmpty(node.ObjToken)
                                            ? $"可用 read_feishu_doc 工具并传入 docToken=\"{node.ObjToken}\" 读取此节点内容"
                                            : "此节点类型暂不支持直接读取内容",
                                    });
                                }
                            }

                            if (response.Data?.HasMore != true) break;
                            pageToken = response.Data.PageToken;
                        }

                        logger.LogInformation(
                            "search_feishu_wiki 成功 spaceId={SpaceId} keyword={Keyword} hitCount={HitCount}",
                            spaceId, keyword, matchedNodes.Count);

                        return (object)new
                        {
                            success = true,
                            spaceId,
                            keyword,
                            totalCount = matchedNodes.Count,
                            nodes = matchedNodes,
                            tip = matchedNodes.Count > 0
                                ? "找到节点后可使用 read_feishu_doc 工具并传入节点的 docToken 读取详细内容。"
                                : "未找到匹配节点，请尝试其他关键词或指定 parentNodeToken 缩小搜索范围。",
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
    /// </summary>
    internal static string ExtractSpaceId(string input)
    {
        input = input.Trim();

        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
            {
                string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return segments.LastOrDefault() ?? string.Empty;
            }
            return string.Empty;
        }

        return input;
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
        _ => objType ?? "未知类型",
    };
}
