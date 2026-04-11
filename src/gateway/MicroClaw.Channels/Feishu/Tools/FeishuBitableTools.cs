using System.ComponentModel;
using System.Text.Json;
using FeishuNetSdk;
using FeishuNetSdk.Base;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-3: 飞书多维表格读取工具 — 提供 <c>read_feishu_bitable</c> AIFunction，
/// Agent 可查询多维表格（Bitable）记录，支持字段过滤与排序。
/// </summary>
public static class FeishuBitableTools
{
    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("read_feishu_bitable", "查询飞书多维表格（Bitable）的记录列表，支持字段过滤与排序。可传入多维表格 URL 或 App Token + Table ID 进行查询。"),
        ("write_feishu_bitable", "向飞书多维表格（Bitable）新增或修改记录。传入 recordId 时为更新操作，不传时为新增操作。机器人应用必须对该表格有展开和编辑权限。"),
    ];

    /// <summary>返回工具元数据（供工具列表 API 使用）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 根据已启用的飞书渠道配置创建多维表格读取工具实例。
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateTools(FeishuChannelSettings settings, IFeishuTenantApi api, ILogger logger)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("多维表格 URL（如 https://xxxx.feishu.cn/base/xxxxxx?table=tblXXX）或 App Token（仅字母/数字/下划线/横线，与 tableId 一同使用）")] string bitableUrlOrToken,
                    [Description("Table ID（格式 tblXXXX，当 bitableUrlOrToken 不含 URL 时必填；当传入完整 URL 且 URL 中含 table= 参数时可省略）")] string tableId = "",
                    [Description("飞书筛选表达式（可选），例如：CurrentValue.[状态]=\"进行中\"；多条件用 AND/OR 组合，如 AND(CurrentValue.[优先级]=\"P0\",CurrentValue.[状态]=\"待处理\"）")] string filter = "",
                    [Description("排序规则 JSON 字符串（可选），格式为 [{field_name: \"字段名\", desc: true}]，不传则使用默认排序")] string sort = "",
                    [Description("单次返回的最大记录数，范围 1-200，默认 20")] int pageSize = 20,
                    CancellationToken ct = default) =>
                {
                    try
                    {
                        (string appToken, string resolvedTableId) = ExtractBitableIds(bitableUrlOrToken, tableId);

                        if (string.IsNullOrWhiteSpace(appToken))
                            return (object)new { success = false, error = "无法解析多维表格 App Token，请提供有效的飞书多维表格 URL 或 App Token。" };

                        if (string.IsNullOrWhiteSpace(resolvedTableId))
                            return (object)new { success = false, error = "无法解析 Table ID，请在 URL 中包含 table= 参数，或在 tableId 参数中单独传入。" };

                        if (!IsValidToken(appToken))
                            return (object)new { success = false, error = "App Token 格式不正确，只允许字母、数字、下划线和横线。" };

                        if (!IsValidToken(resolvedTableId))
                            return (object)new { success = false, error = "Table ID 格式不正确，只允许字母、数字、下划线和横线。" };

                        // F-G-3: Whitelist check (only restrict when configured)
                        if (settings.AllowedBitableTokens.Length > 0 &&
                            !settings.AllowedBitableTokens.Contains(appToken, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该多维表格 App Token 不在渠道允许的白名单内，Agent 无权查询此表格。" };
                        }

                        int clampedPageSize = Math.Clamp(pageSize, 1, 200);

                        // SDK call — filter/sort passed as query parameters via the GET records API
                        var response = await api.GetBitableV1AppsByAppTokenTablesByTableIdRecordsAsync(
                            appToken, resolvedTableId,
                            filter: string.IsNullOrWhiteSpace(filter) ? null : filter,
                            sort: string.IsNullOrWhiteSpace(sort) ? null : sort,
                            page_size: clampedPageSize);

                        if (response.Code != 0)
                        {
                            logger.LogWarning("飞书多维表格查询 API 返回错误 appToken={AppToken} tableId={TableId}: {Msg}",
                                appToken, resolvedTableId, response.Msg);
                            return (object)new { success = false, error = response.Msg ?? "查询多维表格失败，请确认 App Token/Table ID 正确且机器人有数据表读取权限。" };
                        }

                        var data = response.Data;
                        var items = data?.Items?.Select(r => new Dictionary<string, object?>
                        {
                            ["record_id"] = r.RecordId,
                            ["fields"] = r.Fields,
                        }).ToList() ?? [];

                        logger.LogInformation(
                            "read_feishu_bitable 成功 appToken={AppToken} tableId={TableId} records={RecordCount} hasMore={HasMore}",
                            appToken, resolvedTableId, items.Count, data?.HasMore);

                        return (object)new
                        {
                            success = true,
                            appToken,
                            tableId = resolvedTableId,
                            total = data?.Total ?? 0,
                            hasMore = data?.HasMore ?? false,
                            pageToken = data?.PageToken,
                            recordCount = items.Count,
                            records = items,
                        };
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "read_feishu_bitable 执行失败");
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "read_feishu_bitable",
                description: "查询飞书多维表格（Bitable）的记录列表，支持字段过滤与排序。可传入多维表格 URL 或 App Token + Table ID 进行查询。"),
        ];
    }

    /// <summary>
    /// 从飞书多维表格 URL 中提取 AppToken 和 TableId。
    /// URL 格式：https://xxxx.feishu.cn/base/{appToken}?table={tableId}&amp;view={viewId}
    /// 或直接传入 AppToken，TableId 从 tableId 参数取。
    /// </summary>
    internal static (string AppToken, string TableId) ExtractBitableIds(string urlOrToken, string fallbackTableId)
    {
        urlOrToken = urlOrToken.Trim();
        fallbackTableId = fallbackTableId.Trim();

        if (urlOrToken.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            urlOrToken.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(urlOrToken, UriKind.Absolute, out Uri? uri))
                return (string.Empty, fallbackTableId);

            string appToken = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;

            string tableIdFromUrl = fallbackTableId;
            string query = uri.Query.TrimStart('?');
            foreach (string part in query.Split('&'))
            {
                if (part.StartsWith("table=", StringComparison.OrdinalIgnoreCase))
                {
                    tableIdFromUrl = Uri.UnescapeDataString(part["table=".Length..]);
                    break;
                }
            }

            return (appToken, tableIdFromUrl);
        }

        return (urlOrToken, fallbackTableId);
    }

    /// <summary>
    /// 验证 Token 格式：只允许字母、数字、下划线和横线，防止路径注入。
    /// </summary>
    internal static bool IsValidToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        foreach (char c in token)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                return false;
        }
        return true;
    }

    // -----------------------------------------------------------------------
    // F-C-4: 多维表格写入工具
    // -----------------------------------------------------------------------

    /// <summary>创建多维表格写入工具实例列表。</summary>
    internal static IReadOnlyList<AIFunction> CreateWriteTools(FeishuChannelSettings settings, IFeishuTenantApi api, ILogger logger)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("多维表格 URL（如 https://xxxx.feishu.cn/base/xxxxxx?table=tblXXX）或 App Token（仅字母/数字/下划线/横线，与 tableId 一同使用）")] string bitableUrlOrToken,
                    [Description("字段内容 JSON 字符串，格式为 {\"字段名\": \"字段值\", ...}，例如 {\"状态\": \"进行中\", \"负责人\": \"张三\"}")] string fields,
                    [Description("Table ID（格式 tblXXXX，当 bitableUrlOrToken 不含 URL 时必填；当传入完整 URL 且 URL 中含 table= 参数时可省略）")] string tableId = "",
                    [Description("记录 ID（格式 recXXXX），传入时为更新现有记录，不传时为新增记录")] string recordId = "",
                    CancellationToken ct = default) =>
                {
                    try
                    {
                        (string appToken, string resolvedTableId) = ExtractBitableIds(bitableUrlOrToken, tableId);

                        if (string.IsNullOrWhiteSpace(appToken))
                            return (object)new { success = false, error = "无法解析多维表格 App Token，请提供有效的飞书多维表格 URL 或 App Token。" };

                        if (string.IsNullOrWhiteSpace(resolvedTableId))
                            return (object)new { success = false, error = "无法解析 Table ID，请在 URL 中包含 table= 参数，或在 tableId 参数中单独传入。" };

                        if (!IsValidToken(appToken))
                            return (object)new { success = false, error = "App Token 格式不正确，只允许字母、数字、下划线和横线。" };

                        if (!IsValidToken(resolvedTableId))
                            return (object)new { success = false, error = "Table ID 格式不正确，只允许字母、数字、下划线和横线。" };

                        if (settings.AllowedBitableTokens.Length > 0 &&
                            !settings.AllowedBitableTokens.Contains(appToken, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该多维表格 App Token 不在渠道允许的白名单内，Agent 无权写入此表格。" };
                        }

                        string trimmedRecordId = recordId.Trim();
                        if (!string.IsNullOrEmpty(trimmedRecordId) && !IsValidToken(trimmedRecordId))
                            return (object)new { success = false, error = "Record ID 格式不正确，只允许字母、数字、下划线和横线。" };

                        if (string.IsNullOrWhiteSpace(fields))
                            return (object)new { success = false, error = "fields 内容不能为空，请提供字段内容 JSON。" };

                        JsonElement fieldsElement;
                        try
                        {
                            using var doc = JsonDocument.Parse(fields);
                            fieldsElement = doc.RootElement.Clone();
                        }
                        catch
                        {
                            return (object)new { success = false, error = "fields 参数不是合法的 JSON 字符串，请检查格式。" };
                        }

                        if (fieldsElement.ValueKind != JsonValueKind.Object)
                            return (object)new { success = false, error = "fields 必须是 JSON 对象，例如 {\"字段名\": \"字段值\"}" };

                        // Deserialize fields to Dictionary for SDK body DTO
                        var fieldsDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldsElement.GetRawText());

                        bool isCreate = string.IsNullOrEmpty(trimmedRecordId);

                        if (isCreate)
                        {
                            var createBody = new PostBitableV1AppsByAppTokenTablesByTableIdRecordsBodyDto
                            {
                                Fields = fieldsDict!,
                            };
                            var response = await api.PostBitableV1AppsByAppTokenTablesByTableIdRecordsAsync(
                                appToken, resolvedTableId, createBody);

                            if (response.Code != 0)
                            {
                                logger.LogWarning("飞书多维表格新增记录 API 返回错误 appToken={AppToken} tableId={TableId}: {Msg}",
                                    appToken, resolvedTableId, response.Msg);
                                return (object)new { success = false, error = response.Msg ?? "新增多维表格记录失败，请确认 App Token/Table ID 正确且机器人有展开和编辑权限。" };
                            }

                            string? resultRecordId = response.Data?.Record?.RecordId;
                            logger.LogInformation(
                                "write_feishu_bitable 成功 appToken={AppToken} tableId={TableId} recordId={RecordId} operation=create",
                                appToken, resolvedTableId, resultRecordId);

                            return (object)new
                            {
                                success = true,
                                appToken,
                                tableId = resolvedTableId,
                                recordId = resultRecordId,
                                operation = "created",
                            };
                        }
                        else
                        {
                            var updateBody = new PutBitableV1AppsByAppTokenTablesByTableIdRecordsByRecordIdBodyDto
                            {
                                Fields = fieldsDict!,
                            };
                            var response = await api.PutBitableV1AppsByAppTokenTablesByTableIdRecordsByRecordIdAsync(
                                appToken, resolvedTableId, trimmedRecordId, updateBody);

                            if (response.Code != 0)
                            {
                                logger.LogWarning("飞书多维表格更新记录 API 返回错误 appToken={AppToken} tableId={TableId} recordId={RecordId}: {Msg}",
                                    appToken, resolvedTableId, trimmedRecordId, response.Msg);
                                return (object)new { success = false, error = response.Msg ?? "更新多维表格记录失败，请确认 App Token/Table ID/Record ID 正确且机器人有展开和编辑权限。" };
                            }

                            string? resultRecordId = response.Data?.Record?.RecordId ?? trimmedRecordId;
                            logger.LogInformation(
                                "write_feishu_bitable 成功 appToken={AppToken} tableId={TableId} recordId={RecordId} operation=update",
                                appToken, resolvedTableId, resultRecordId);

                            return (object)new
                            {
                                success = true,
                                appToken,
                                tableId = resolvedTableId,
                                recordId = resultRecordId,
                                operation = "updated",
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "write_feishu_bitable 执行失败");
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "write_feishu_bitable",
                description: "向飞书多维表格（Bitable）新增或修改记录。传入 recordId 时为更新操作，不传时为新增操作。机器人应用必须对该表格有展开和编辑权限。"),
        ];
    }
}
