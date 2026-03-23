using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-9: 飞书审批工具 — 提供 <c>submit_feishu_approval</c> 和 <c>get_feishu_approval_status</c> AIFunction，
/// Agent 可代用户提交飞书审批单或查询现有审批实例的状态。
/// 飞书 API 参考：
///   POST /open-apis/approval/v4/instances              （创建审批实例）
///   GET  /open-apis/approval/v4/instances/{instance_code} （查询审批实例）
/// 权限要求：approval:approval:write（提交）、approval:approval:readonly（查询）
/// </summary>
public static class FeishuApprovalTools
{
    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("submit_feishu_approval",      "代用户在飞书提交一个审批单，需提供审批定义 Code（approval_code）、提交人 OpenID 及表单字段值（JSON 对象）。提交成功后返回审批实例 Code（instance_code）。"),
        ("get_feishu_approval_status",  "通过审批实例 Code（instance_code）查询飞书审批单的当前状态（待审批/已通过/已拒绝/已撤回）及审批人列表。"),
    ];

    /// <summary>返回工具元数据（供工具列表 API 使用）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 根据已启用的飞书渠道配置创建审批工具实例列表（提交 + 查询）。
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateTools(FeishuChannelSettings settings, ILogger logger)
    {
        return
        [
            // ── submit_feishu_approval ──────────────────────────────────────────
            AIFunctionFactory.Create(
                async (
                    [Description("审批定义 Code（approval_code），由飞书审批管理后台创建，格式为字母/数字/横线，如 A8B3C4D5-XXXX-XXXX-XXXX-XXXXXXXXXXXX")] string approvalCode,
                    [Description("提交人的飞书 open_id（ou_ 前缀），代表该用户发起此审批")] string openId,
                    [Description("审批表单字段值，JSON 对象格式，键为字段 ID（widget_id），值为对应的字段值。例如：{\"widget_xxxx\": \"请假事由\", \"widget_yyyy\": \"2024-01-15\"}")] string formValues,
                    CancellationToken ct = default) =>
                {
                    try
                    {
                        // 1. 安全校验：approval_code 格式
                        if (string.IsNullOrWhiteSpace(approvalCode) || !IsValidApprovalCode(approvalCode))
                        {
                            return (object)new { success = false, error = "審批定义 Code 格式不正确，只允许字母、数字和横线。" };
                        }

                        // 2. 安全校验：openId 格式（必须 ou_ 开头）
                        if (string.IsNullOrWhiteSpace(openId) || !openId.StartsWith("ou_", StringComparison.Ordinal))
                        {
                            return (object)new { success = false, error = "提交人 open_id 格式不正确，必须以 ou_ 开头。" };
                        }

                        // 3. 白名单校验（配置非空时才限制）
                        if (settings.AllowedApprovalCodes.Length > 0 &&
                            !settings.AllowedApprovalCodes.Contains(approvalCode, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该审批定义 Code 不在渠道允许的白名单内，Agent 无权提交此类型审批。" };
                        }

                        // 4. formValues 格式校验（必须是合法 JSON 对象）
                        JsonElement formJson;
                        try
                        {
                            using var tempDoc = JsonDocument.Parse(formValues);
                            if (tempDoc.RootElement.ValueKind != JsonValueKind.Object)
                            {
                                return (object)new { success = false, error = "formValues 必须是 JSON 对象格式（{...}）。" };
                            }
                            formJson = tempDoc.RootElement.Clone();
                        }
                        catch (JsonException)
                        {
                            return (object)new { success = false, error = "formValues 不是合法的 JSON 字符串，请检查格式。" };
                        }

                        // 5. 获取 Tenant Access Token
                        string? tenantToken = await GetTenantAccessTokenAsync(settings, logger, ct);
                        if (string.IsNullOrWhiteSpace(tenantToken))
                        {
                            return (object)new { success = false, error = "获取飞书 Tenant Access Token 失败，请检查渠道 AppId/AppSecret 配置。" };
                        }

                        // 6. 提交审批
                        string? instanceCode = await CreateApprovalInstanceAsync(
                            settings.ApiBaseUrl, tenantToken, approvalCode, openId, formJson, logger, ct);

                        if (instanceCode is null)
                        {
                            return (object)new { success = false, error = "提交飞书审批失败，请确认 approval_code 正确且机器人应用已申请 approval:approval:write 权限。" };
                        }

                        logger.LogInformation(
                            "submit_feishu_approval 成功 approvalCode={ApprovalCode} openId={OpenId} instanceCode={InstanceCode}",
                            approvalCode, openId, instanceCode);

                        return (object)new
                        {
                            success = true,
                            approvalCode,
                            openId,
                            instanceCode,
                            tip = "审批已提交，可使用 get_feishu_approval_status 工具传入 instanceCode 查询审批进度。",
                        };
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "submit_feishu_approval 执行失败");
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "submit_feishu_approval",
                description: "代用户在飞书提交一个审批单，需提供审批定义 Code（approval_code）、提交人 OpenID 及表单字段值（JSON 对象）。提交成功后返回审批实例 Code（instance_code）。"),

            // ── get_feishu_approval_status ──────────────────────────────────────
            AIFunctionFactory.Create(
                async (
                    [Description("审批实例 Code（instance_code），提交审批后返回，或从飞书审批列表页获取")] string instanceCode,
                    CancellationToken ct = default) =>
                {
                    try
                    {
                        // 1. 安全校验：instance_code 格式
                        if (string.IsNullOrWhiteSpace(instanceCode) || !IsValidInstanceCode(instanceCode))
                        {
                            return (object)new { success = false, error = "审批实例 Code 格式不正确，只允许字母、数字和横线。" };
                        }

                        // 2. 获取 Tenant Access Token
                        string? tenantToken = await GetTenantAccessTokenAsync(settings, logger, ct);
                        if (string.IsNullOrWhiteSpace(tenantToken))
                        {
                            return (object)new { success = false, error = "获取飞书 Tenant Access Token 失败，请检查渠道 AppId/AppSecret 配置。" };
                        }

                        // 3. 查询审批实例
                        var detail = await GetApprovalInstanceAsync(
                            settings.ApiBaseUrl, tenantToken, instanceCode, logger, ct);

                        if (detail is null)
                        {
                            return (object)new { success = false, error = "查询飞书审批实例失败，请确认 instance_code 正确且机器人应用已申请 approval:approval:readonly 权限。" };
                        }

                        logger.LogInformation(
                            "get_feishu_approval_status 成功 instanceCode={InstanceCode} status={Status}",
                            instanceCode, detail.Status);

                        return (object)new
                        {
                            success = true,
                            instanceCode,
                            status         = detail.Status,
                            statusLabel    = MapStatusLabel(detail.Status),
                            approvalCode   = detail.ApprovalCode,
                            startTime      = detail.StartTime,
                            endTime        = detail.EndTime,
                            approverList   = detail.ApproverList,
                            ccList         = detail.CcList,
                        };
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "get_feishu_approval_status 执行失败");
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "get_feishu_approval_status",
                description: "通过审批实例 Code（instance_code）查询飞书审批单的当前状态（待审批/已通过/已拒绝/已撤回）及审批人列表。"),
        ];
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 私有辅助方法
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>校验审批定义 Code 格式（字母/数字/横线，防路径注入）。</summary>
    internal static bool IsValidApprovalCode(string code) =>
        !string.IsNullOrWhiteSpace(code) &&
        code.Length <= 128 &&
        System.Text.RegularExpressions.Regex.IsMatch(code, @"^[a-zA-Z0-9\-_]+$");

    /// <summary>校验审批实例 Code 格式（字母/数字/横线，防路径注入）。</summary>
    internal static bool IsValidInstanceCode(string code) =>
        !string.IsNullOrWhiteSpace(code) &&
        code.Length <= 128 &&
        System.Text.RegularExpressions.Regex.IsMatch(code, @"^[a-zA-Z0-9\-_]+$");

    /// <summary>将飞书审批状态码映射为中文可读标签。</summary>
    private static string MapStatusLabel(string? status) => status switch
    {
        "PENDING"   => "审批中",
        "APPROVED"  => "已通过",
        "REJECTED"  => "已拒绝",
        "CANCELED"  => "已撤回",
        "DELETED"   => "已删除",
        _           => status ?? "未知",
    };

    /// <summary>
    /// 获取飞书 Tenant Access Token（与其他工具相同逻辑）。
    /// </summary>
    private static async Task<string?> GetTenantAccessTokenAsync(
        FeishuChannelSettings settings,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.BaseAddress = new Uri(settings.ApiBaseUrl);

            var body = JsonSerializer.Serialize(new
            {
                app_id     = settings.AppId,
                app_secret = settings.AppSecret,
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(
                "/open-apis/auth/v3/tenant_access_token/internal", content, ct);

            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tenant_access_token", out var tokenEl))
                return tokenEl.GetString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "获取飞书 Tenant Access Token 失败");
        }

        return null;
    }

    /// <summary>
    /// 提交飞书审批实例。
    /// API: POST /open-apis/approval/v4/instances
    /// </summary>
    private static async Task<string?> CreateApprovalInstanceAsync(
        string apiBaseUrl,
        string tenantToken,
        string approvalCode,
        string openId,
        JsonElement formJson,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.BaseAddress = new Uri(apiBaseUrl);
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {tenantToken}");

            // 将 formJson 对象拆成飞书 form 数组格式：[{id, type, value}]
            // 飞书审批要求 form 字段为 JSON 字符串（stringified array）
            var formFields = new List<object>();
            foreach (var prop in formJson.EnumerateObject())
            {
                formFields.Add(new
                {
                    id    = prop.Name,
                    type  = "input",        // 通用文本类型，实际类型由审批定义决定
                    value = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString()
                            : prop.Value.ToString(),
                });
            }
            string formStr = JsonSerializer.Serialize(formFields);

            var bodyObj = new
            {
                approval_code = approvalCode,
                open_id       = openId,
                form          = formStr,
            };

            string bodyJson = JsonSerializer.Serialize(bodyObj);
            using var httpContent = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("/open-apis/approval/v4/instances", httpContent, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "submit_feishu_approval API 返回 {StatusCode}", (int)response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            int code = doc.RootElement.TryGetProperty("code", out var codeEl) ? codeEl.GetInt32() : -1;
            if (code != 0)
            {
                string msg = doc.RootElement.TryGetProperty("msg", out var msgEl)
                    ? msgEl.GetString() ?? string.Empty : string.Empty;
                logger.LogWarning("submit_feishu_approval API code={Code} msg={Msg}", code, msg);
                return null;
            }

            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("instance_code", out var instEl))
            {
                return instEl.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreateApprovalInstanceAsync 发生异常");
            return null;
        }
    }

    /// <summary>
    /// 查询飞书审批实例详情。
    /// API: GET /open-apis/approval/v4/instances/{instance_code}
    /// </summary>
    private static async Task<ApprovalInstanceDetail?> GetApprovalInstanceAsync(
        string apiBaseUrl,
        string tenantToken,
        string instanceCode,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.BaseAddress = new Uri(apiBaseUrl);
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {tenantToken}");

            string encoded = Uri.EscapeDataString(instanceCode);
            using var response = await http.GetAsync(
                $"/open-apis/approval/v4/instances/{encoded}", ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "get_feishu_approval_status API 返回 {StatusCode}", (int)response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            int code = doc.RootElement.TryGetProperty("code", out var codeEl) ? codeEl.GetInt32() : -1;
            if (code != 0)
            {
                string msg = doc.RootElement.TryGetProperty("msg", out var msgEl)
                    ? msgEl.GetString() ?? string.Empty : string.Empty;
                logger.LogWarning("get_feishu_approval_status API code={Code} msg={Msg}", code, msg);
                return null;
            }

            if (!doc.RootElement.TryGetProperty("data", out var data))
                return null;

            string? status       = data.TryGetProperty("status",        out var sts) ? sts.GetString()   : null;
            string? approvalCode = data.TryGetProperty("approval_code", out var ac)  ? ac.GetString()    : null;
            string? startTime    = data.TryGetProperty("start_time",    out var st)  ? FormatTimestamp(st): null;
            string? endTime      = data.TryGetProperty("end_time",      out var et)  ? FormatTimestamp(et): null;

            var approverList = ExtractUserList(data, "timeline");
            var ccList       = ExtractUserList(data, "cc_list");

            return new ApprovalInstanceDetail(status, approvalCode, startTime, endTime, approverList, ccList);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetApprovalInstanceAsync 发生异常");
            return null;
        }
    }

    /// <summary>从 timeline 或 cc_list 数组中提取用户 open_id 和操作状态。</summary>
    private static List<object> ExtractUserList(JsonElement data, string propertyName)
    {
        var list = new List<object>();
        if (!data.TryGetProperty(propertyName, out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in arr.EnumerateArray())
        {
            string? userId = item.TryGetProperty("open_id",  out var uid) ? uid.GetString()    : null;
            string? status = item.TryGetProperty("status",   out var sts) ? sts.GetString()    : null;
            string? name   = item.TryGetProperty("user_id",  out var unm) ? unm.GetString()    : null;
            list.Add(new { openId = userId, name, status });
        }

        return list;
    }

    /// <summary>将毫秒级 Unix 时间戳元素转为可读时间字符串。</summary>
    private static string? FormatTimestamp(JsonElement el)
    {
        string? raw = el.GetString();
        if (long.TryParse(raw, out long ms))
        {
            // 飞书审批时间戳单位为毫秒
            return DateTimeOffset.FromUnixTimeMilliseconds(ms)
                .ToOffset(TimeSpan.FromHours(8))
                .ToString("yyyy-MM-dd HH:mm:ss");
        }
        return raw;
    }

    // ── 数据传输对象 ──────────────────────────────────────────────────────────

    private sealed record ApprovalInstanceDetail(
        string? Status,
        string? ApprovalCode,
        string? StartTime,
        string? EndTime,
        List<object> ApproverList,
        List<object> CcList);
}
