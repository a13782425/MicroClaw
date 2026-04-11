using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using FeishuNetSdk;
using FeishuNetSdk.Approval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-9: 飞书审批工具 — 提供 <c>submit_feishu_approval</c> 和 <c>get_feishu_approval_status</c> AIFunction，
/// Agent 可代用户提交飞书审批单或查询现有审批实例的状态。
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
    public static IReadOnlyList<AIFunction> CreateTools(FeishuChannelSettings settings, IFeishuTenantApi api, ILogger logger)
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
                        if (string.IsNullOrWhiteSpace(approvalCode) || !IsValidApprovalCode(approvalCode))
                            return (object)new { success = false, error = "审批定义 Code 格式不正确，只允许字母、数字和横线。" };

                        if (string.IsNullOrWhiteSpace(openId) || !openId.StartsWith("ou_", StringComparison.Ordinal))
                            return (object)new { success = false, error = "提交人 open_id 格式不正确，必须以 ou_ 开头。" };

                        if (settings.AllowedApprovalCodes.Length > 0 &&
                            !settings.AllowedApprovalCodes.Contains(approvalCode, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该审批定义 Code 不在渠道允许的白名单内，Agent 无权提交此类型审批。" };
                        }

                        // Validate and transform formValues into Feishu form array format
                        JsonElement formJson;
                        try
                        {
                            using var tempDoc = JsonDocument.Parse(formValues);
                            if (tempDoc.RootElement.ValueKind != JsonValueKind.Object)
                                return (object)new { success = false, error = "formValues 必须是 JSON 对象格式（{...}）。" };
                            formJson = tempDoc.RootElement.Clone();
                        }
                        catch (JsonException)
                        {
                            return (object)new { success = false, error = "formValues 不是合法的 JSON 字符串，请检查格式。" };
                        }

                        // Build form field array as required by Feishu approval API
                        var formFields = new List<object>();
                        foreach (var prop in formJson.EnumerateObject())
                        {
                            formFields.Add(new
                            {
                                id    = prop.Name,
                                type  = "input",
                                value = prop.Value.ValueKind == JsonValueKind.String
                                        ? prop.Value.GetString()
                                        : prop.Value.ToString(),
                            });
                        }
                        string formStr = JsonSerializer.Serialize(formFields);

                        var bodyDto = new PostApprovalV4InstancesBodyDto
                        {
                            ApprovalCode = approvalCode,
                            OpenId = openId,
                            Form = formStr,
                        };

                        var response = await api.PostApprovalV4InstancesAsync(bodyDto);

                        if (response.Code != 0)
                        {
                            logger.LogWarning("submit_feishu_approval API 返回错误: {Msg}", response.Msg);
                            return (object)new { success = false, error = response.Msg ?? "提交飞书审批失败，请确认 approval_code 正确且机器人应用已申请 approval:approval 权限。" };
                        }

                        string? instanceCode = response.Data?.InstanceCode;

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
                        if (string.IsNullOrWhiteSpace(instanceCode) || !IsValidInstanceCode(instanceCode))
                            return (object)new { success = false, error = "审批实例 Code 格式不正确，只允许字母、数字和横线。" };

                        var response = await api.GetApprovalV4InstancesByInstanceIdAsync(instanceCode);

                        if (response.Code != 0)
                        {
                            logger.LogWarning("get_feishu_approval_status API 返回错误: {Msg}", response.Msg);
                            return (object)new { success = false, error = response.Msg ?? "查询飞书审批实例失败，请确认 instance_code 正确且机器人应用已申请 approval:approval:readonly 权限。" };
                        }

                        var data = response.Data;
                        string? status = data?.Status;

                        logger.LogInformation(
                            "get_feishu_approval_status 成功 instanceCode={InstanceCode} status={Status}",
                            instanceCode, status);

                        return (object)new
                        {
                            success = true,
                            instanceCode,
                            status,
                            statusLabel = MapStatusLabel(status),
                            approvalCode = data?.ApprovalCode,
                            approvalName = data?.ApprovalName,
                            serialNumber = data?.SerialNumber,
                            startTime = data?.StartTime,
                            endTime = data?.EndTime,
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

    /// <summary>校验审批定义 Code 格式（字母/数字/横线，防路径注入）。</summary>
    internal static bool IsValidApprovalCode(string code) =>
        !string.IsNullOrWhiteSpace(code) &&
        code.Length <= 128 &&
        Regex.IsMatch(code, @"^[a-zA-Z0-9\-_]+$");

    /// <summary>校验审批实例 Code 格式（字母/数字/横线，防路径注入）。</summary>
    internal static bool IsValidInstanceCode(string code) =>
        !string.IsNullOrWhiteSpace(code) &&
        code.Length <= 128 &&
        Regex.IsMatch(code, @"^[a-zA-Z0-9\-_]+$");

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
}
