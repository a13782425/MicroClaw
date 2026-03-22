using System.ComponentModel;
using System.Text;
using System.Text.Json;
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
        ("write_feishu_bitable", "向飞书多维表格（Bitable）新增或修改记录。传入 recordId 时为更新操佼，不传时为新增操佼。机器人应用必须对该表格有展开和编辑权限。"),
    ];

    /// <summary>返回工具元数据（供工具列表 API 使用）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 根据已启用的飞书渠道配置创建多维表格读取工具实例。
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateTools(FeishuChannelSettings settings, ILogger logger)
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
                        // 1. 解析 AppToken 和 TableId
                        (string appToken, string resolvedTableId) = ExtractBitableIds(bitableUrlOrToken, tableId);

                        if (string.IsNullOrWhiteSpace(appToken))
                        {
                            return (object)new { success = false, error = "无法解析多维表格 App Token，请提供有效的飞书多维表格 URL 或 App Token。" };
                        }

                        if (string.IsNullOrWhiteSpace(resolvedTableId))
                        {
                            return (object)new { success = false, error = "无法解析 Table ID，请在 URL 中包含 table= 参数，或在 tableId 参数中单独传入。" };
                        }

                        // 2. 安全校验（防路径注入）
                        if (!IsValidToken(appToken))
                        {
                            return (object)new { success = false, error = "App Token 格式不正确，只允许字母、数字、下划线和横线。" };
                        }

                        if (!IsValidToken(resolvedTableId))
                        {
                            return (object)new { success = false, error = "Table ID 格式不正确，只允许字母、数字、下划线和横线。" };
                        }

                        // F-G-3: 白名单校验（配置非空时才限制）
                        if (settings.AllowedBitableTokens.Length > 0 &&
                            !settings.AllowedBitableTokens.Contains(appToken, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该多维表格 App Token 不在渠道允许的白名单内，Agent 无权查询此表格。" };
                        }

                        // 3. 限制 pageSize 范围
                        int clampedPageSize = Math.Clamp(pageSize, 1, 200);

                        // 4. 获取 Tenant Access Token
                        string? tenantToken = await GetTenantAccessTokenAsync(settings, logger, ct);
                        if (string.IsNullOrWhiteSpace(tenantToken))
                        {
                            return (object)new { success = false, error = "获取飞书 Tenant Access Token 失败，请检查渠道 AppId/AppSecret 配置。" };
                        }

                        // 5. 查询多维表格记录
                        var result = await QueryBitableRecordsAsync(
                            settings.ApiBaseUrl, appToken, resolvedTableId,
                            tenantToken, filter, sort, clampedPageSize, logger, ct);

                        if (result is null)
                        {
                            return (object)new { success = false, error = "查询多维表格失败，请确认 App Token/Table ID 正确且机器人有数据表读取权限。" };
                        }

                        logger.LogInformation(
                            "read_feishu_bitable 成功 appToken={AppToken} tableId={TableId} records={RecordCount} hasMore={HasMore}",
                            appToken, resolvedTableId, result.Items.Count, result.HasMore);

                        return (object)new
                        {
                            success = true,
                            appToken,
                            tableId = resolvedTableId,
                            total = result.Total,
                            hasMore = result.HasMore,
                            pageToken = result.PageToken,
                            recordCount = result.Items.Count,
                            records = result.Items,
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
    /// URL 格式：https://xxxx.feishu.cn/base/{appToken}?table={tableId}&view={viewId}
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

            // 路径最后一段为 AppToken（/base/{token}）
            string appToken = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;

            // 从查询字符串取 table= 参数
            string tableId = fallbackTableId;
            string query = uri.Query.TrimStart('?');
            foreach (string part in query.Split('&'))
            {
                if (part.StartsWith("table=", StringComparison.OrdinalIgnoreCase))
                {
                    tableId = Uri.UnescapeDataString(part["table=".Length..]);
                    break;
                }
            }

            return (appToken, tableId);
        }

        // 非 URL，直接当作 AppToken，TableId 使用 fallback
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
    /// 调用飞书多维表格查询接口。
    /// GET /open-apis/bitable/v1/apps/{app_token}/tables/{table_id}/records
    /// </summary>
    private static async Task<BitableQueryResult?> QueryBitableRecordsAsync(
        string? apiBaseUrl, string appToken, string tableId,
        string tenantToken, string filter, string sort, int pageSize,
        ILogger logger, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tenantToken);

        string baseUrl = (apiBaseUrl ?? "https://open.feishu.cn").TrimEnd('/');
        // appToken 和 tableId 已通过 IsValidToken 验证，仅含字母/数字/下划线/横线
        var queryString = new StringBuilder();
        queryString.Append($"?page_size={pageSize}");
        if (!string.IsNullOrWhiteSpace(filter))
            queryString.Append($"&filter={Uri.EscapeDataString(filter)}");
        if (!string.IsNullOrWhiteSpace(sort))
            queryString.Append($"&sort={Uri.EscapeDataString(sort)}");

        string url = $"{baseUrl}/open-apis/bitable/v1/apps/{appToken}/tables/{tableId}/records{queryString}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
            {
                logger.LogWarning("飞书多维表格查询 API 返回错误 appToken={AppToken} tableId={TableId}: {Body}",
                    appToken, tableId, body);
                return null;
            }

            if (!doc.RootElement.TryGetProperty("data", out var dataEl))
            {
                logger.LogWarning("飞书多维表格查询响应格式异常: {Body}", body);
                return null;
            }

            int total = dataEl.TryGetProperty("total", out var totalEl) ? totalEl.GetInt32() : 0;
            bool hasMore = dataEl.TryGetProperty("has_more", out var hasMoreEl) && hasMoreEl.GetBoolean();
            string? pageToken = dataEl.TryGetProperty("page_token", out var ptEl) ? ptEl.GetString() : null;

            var items = new List<Dictionary<string, object?>>();
            if (dataEl.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsEl.EnumerateArray())
                {
                    string? recordId = item.TryGetProperty("record_id", out var ridEl) ? ridEl.GetString() : null;
                    var fields = new Dictionary<string, object?>();

                    if (item.TryGetProperty("fields", out var fieldsEl) &&
                        fieldsEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var field in fieldsEl.EnumerateObject())
                        {
                            fields[field.Name] = ExtractFieldValue(field.Value);
                        }
                    }

                    items.Add(new Dictionary<string, object?>
                    {
                        ["record_id"] = recordId,
                        ["fields"] = fields,
                    });
                }
            }

            return new BitableQueryResult(items, total, hasMore, pageToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "飞书多维表格查询网络请求失败 appToken={AppToken} tableId={TableId}", appToken, tableId);
            return null;
        }
    }

    /// <summary>
    /// 将 JsonElement 字段值转换为可序列化的 .NET 对象：
    /// 字符串数组（单选/多选）→ string/List；数字 → double；布尔 → bool；其余 → ToString。
    /// </summary>
    private static object? ExtractFieldValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Array => ExtractArrayValue(el),
        // 对象类型（附件、人员等复杂字段）转为 JSON 字符串，方便 AI 理解
        _ => el.GetRawText(),
    };

    private static object? ExtractArrayValue(JsonElement el)
    {
        var list = new List<object?>();
        foreach (var item in el.EnumerateArray())
            list.Add(ExtractFieldValue(item));

        // 全为字符串时合并为逗号分隔（单选/多选场景）
        if (list.Count > 0 && list.All(x => x is string))
            return string.Join(", ", list.Cast<string>());

        return list.Count == 0 ? null : list;
    }

    private sealed record BitableQueryResult(
        List<Dictionary<string, object?>> Items,
        int Total,
        bool HasMore,
        string? PageToken);

    // -----------------------------------------------------------------------
    // F-C-4: 多维表格写入工具
    // -----------------------------------------------------------------------

    /// <summary>创建多维表格写入工具实例列表。</summary>
    internal static IReadOnlyList<AIFunction> CreateWriteTools(FeishuChannelSettings settings, ILogger logger)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("多维表格 URL（如 https://xxxx.feishu.cn/base/xxxxxx?table=tblXXX）或 App Token（仅字母/数字/下划线/横线，与 tableId 一同使用）")] string bitableUrlOrToken,
                    [Description("字段内容 JSON 字符串，格式为 {\"\u5b57段名\": \"\u5b57段值\", ...}，例如 {\"\u72b6态\": \"\u8fdb行中\", \"\u8d1f责人\": \"\u5f20三\"}")] string fields,
                    [Description("Table ID（格式 tblXXXX，当 bitableUrlOrToken 不含 URL 时必填；当传入完整 URL 且 URL 中含 table= 参数时可省略）")] string tableId = "",
                    [Description("记录 ID（格式 recXXXX），传入时为更新现有记录，不传时为新增记录")] string recordId = "",
                    CancellationToken ct = default) =>
                {
                    try
                    {
                        // 1. 解析 AppToken 和 TableId
                        (string appToken, string resolvedTableId) = ExtractBitableIds(bitableUrlOrToken, tableId);

                        if (string.IsNullOrWhiteSpace(appToken))
                            return (object)new { success = false, error = "无法解析多维表格 App Token，请提供有效的飞书多维表格 URL 或 App Token。" };

                        if (string.IsNullOrWhiteSpace(resolvedTableId))
                            return (object)new { success = false, error = "无法解析 Table ID，请在 URL 中包含 table= 参数，或在 tableId 参数中单独传入。" };

                        if (!IsValidToken(appToken))
                            return (object)new { success = false, error = "App Token 格式不正确，只允许字母、数字、下划线和横线。" };

                        if (!IsValidToken(resolvedTableId))
                            return (object)new { success = false, error = "Table ID 格式不正确，只允许字母、数字、下划线和横线。" };

                        // F-G-3: 白名单校验（配置非空时才限制）
                        if (settings.AllowedBitableTokens.Length > 0 &&
                            !settings.AllowedBitableTokens.Contains(appToken, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该多维表格 App Token 不在渠道允许的白名单内，Agent 无权写入此表格。" };
                        }

                        // 2. recordId 安全校验（非空时才校验）
                        string trimmedRecordId = recordId.Trim();
                        if (!string.IsNullOrEmpty(trimmedRecordId) && !IsValidToken(trimmedRecordId))
                            return (object)new { success = false, error = "Record ID 格式不正确，只允许字母、数字、下划线和横线。" };

                        // 3. 解析 fields JSON
                        if (string.IsNullOrWhiteSpace(fields))
                            return (object)new { success = false, error = "fields 内容不能为空，请提供字段内容 JSON。" };

                        JsonDocument? fieldsDoc;
                        try { fieldsDoc = JsonDocument.Parse(fields); }
                        catch { return (object)new { success = false, error = "fields 参数不是合法的 JSON 字符串，请检查格式。" }; }

                        if (fieldsDoc.RootElement.ValueKind != JsonValueKind.Object)
                        {
                            fieldsDoc.Dispose();
                            return (object)new { success = false, error = "fields 必须是 JSON 对象，例如 {\"\u5b57段名\": \"\u5b57段值\"}" };
                        }

                        // 4. 获取 Tenant Access Token
                        string? tenantToken = await GetTenantAccessTokenAsync(settings, logger, ct);
                        if (string.IsNullOrWhiteSpace(tenantToken))
                        {
                            fieldsDoc.Dispose();
                            return (object)new { success = false, error = "获取飞书 Tenant Access Token 失败，请检查渠道 AppId/AppSecret 配置。" };
                        }

                        // 5. 写入记录
                        using (fieldsDoc)
                        {
                            string? resultRecordId = await CreateOrUpdateBitableRecordAsync(
                                settings.ApiBaseUrl, appToken, resolvedTableId,
                                tenantToken, fieldsDoc.RootElement, trimmedRecordId, logger, ct);

                            if (resultRecordId is null)
                            {
                                string op = string.IsNullOrEmpty(trimmedRecordId) ? "新增" : "更新";
                                return (object)new { success = false, error = $"{op}多维表格记录失败，请确认 App Token/Table ID 正确且机器人有展开和编辑权限。" };
                            }

                            bool isCreate = string.IsNullOrEmpty(trimmedRecordId);
                            logger.LogInformation(
                                "write_feishu_bitable 成功 appToken={AppToken} tableId={TableId} recordId={RecordId} operation={Op}",
                                appToken, resolvedTableId, resultRecordId, isCreate ? "create" : "update");

                            return (object)new
                            {
                                success = true,
                                appToken,
                                tableId = resolvedTableId,
                                recordId = resultRecordId,
                                operation = isCreate ? "created" : "updated",
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
                description: "向飞书多维表格（Bitable）新增或修改记录。传入 recordId 时为更新操佼，不传时为新增操佼。机器人应用必须对该表格有展开和编辑权限。"),
        ];
    }

    /// <summary>
    /// 创建或更新多维表格记录，返回最终的 record_id（失败时返回 null）。
    /// - recordId 为空:新增（POST /open-apis/bitable/v1/apps/{appToken}/tables/{tableId}/records）
    /// - recordId 非空:更新（PUT  /open-apis/bitable/v1/apps/{appToken}/tables/{tableId}/records/{recordId}）
    /// </summary>
    private static async Task<string?> CreateOrUpdateBitableRecordAsync(
        string? apiBaseUrl, string appToken, string tableId,
        string tenantToken, JsonElement fieldsElement,
        string recordId, ILogger logger, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tenantToken);

        string baseUrl = (apiBaseUrl ?? "https://open.feishu.cn").TrimEnd('/');
        // appToken、tableId、recordId 均已通过 IsValidToken 验证
        bool isCreate = string.IsNullOrEmpty(recordId);
        string url = isCreate
            ? $"{baseUrl}/open-apis/bitable/v1/apps/{appToken}/tables/{tableId}/records"
            : $"{baseUrl}/open-apis/bitable/v1/apps/{appToken}/tables/{tableId}/records/{recordId}";

        // 构建请求体：{"fields": {...}}
        string payload = JsonSerializer.Serialize(new { fields = fieldsElement });
        using var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            HttpResponseMessage response = isCreate
                ? await client.PostAsync(url, httpContent, ct)
                : await client.PutAsync(url, httpContent, ct);

            using (response)
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                using var respDoc = JsonDocument.Parse(body);

                if (respDoc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 0)
                {
                    logger.LogWarning(
                        "飞书多维表格写入 API 返回错误 appToken={AppToken} tableId={TableId} op={Op}: {Body}",
                        appToken, tableId, isCreate ? "create" : "update", body);
                    return null;
                }

                // 新增返回 data.record.record_id；更新返回 data.record.record_id 或直接佼为已知 recordId
                if (respDoc.RootElement.TryGetProperty("data", out var dataEl) &&
                    dataEl.TryGetProperty("record", out var recordEl) &&
                    recordEl.TryGetProperty("record_id", out var ridEl))
                {
                    return ridEl.GetString();
                }

                // 更新场景下 API 应返回 record_id，如未返回则用传入的 recordId
                return isCreate ? null : recordId;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "飞书多维表格写入网络请求失败 appToken={AppToken} tableId={TableId}", appToken, tableId);
            return null;
        }
    }
}
