using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-8: 飞书日历工具 — 提供 <c>get_feishu_calendar</c> 和 <c>create_feishu_event</c> AIFunction，
/// Agent 可查询日历事件列表或创建新的会议/提醒事项。
/// 飞书 API 参考：
///   GET  /open-apis/calendar/v4/calendars/{calendar_id}/events
///   POST /open-apis/calendar/v4/calendars/{calendar_id}/events
/// </summary>
public static class FeishuCalendarTools
{
    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("get_feishu_calendar",  "查询飞书日历中的事件列表，支持指定时间范围（ISO 8601 格式）和最大返回条数。可传入日历 URL 或 Calendar ID。"),
        ("create_feishu_event",  "在飞书日历中创建新事件（会议/提醒）。支持指定标题、开始/结束时间、描述、地点及是否全天事件。"),
    ];

    /// <summary>返回工具元数据（供工具列表 API 使用）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 根据已启用的飞书渠道配置创建日历工具实例列表（查询 + 创建）。
    /// </summary>
    public static IReadOnlyList<AIFunction> CreateTools(FeishuChannelSettings settings, ILogger logger)
    {
        return
        [
            // ── get_feishu_calendar ──────────────────────────────────────────────
            AIFunctionFactory.Create(
                async (
                    [Description("日历 URL（如 https://xxxx.feishu.cn/calendar/XXXX）或 Calendar ID（字母/数字/点/横线/@符号组成的字符串，如 feishu.cn_xxx@group.calendar.feishu.cn）")] string calendarIdOrUrl,
                    [Description("查询起始时间，ISO 8601 格式（如 2024-01-15T09:00:00+08:00）；不传则默认为当前时间")] string startTime = "",
                    [Description("查询截止时间，ISO 8601 格式（如 2024-01-22T18:00:00+08:00）；不传则默认为 7 天后")] string endTime = "",
                    [Description("单次返回的最大事件数，范围 1-100，默认 20")] int pageSize = 20,
                    CancellationToken ct = default) =>
                {
                    try
                    {
                        // 1. 解析 CalendarId
                        string calendarId = ExtractCalendarId(calendarIdOrUrl);
                        if (string.IsNullOrWhiteSpace(calendarId))
                        {
                            return (object)new { success = false, error = "无法解析日历 ID，请提供有效的飞书日历 URL 或 Calendar ID。" };
                        }

                        // 2. 安全校验（防路径注入）
                        if (!IsValidCalendarId(calendarId))
                        {
                            return (object)new { success = false, error = "Calendar ID 格式不正确，只允许字母、数字、点、横线和 @ 符号。" };
                        }

                        // 3. 白名单校验（配置非空时才限制）
                        if (settings.AllowedCalendarIds.Length > 0 &&
                            !settings.AllowedCalendarIds.Contains(calendarId, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该 Calendar ID 不在渠道允许的白名单内，Agent 无权访问此日历。" };
                        }

                        // 4. 解析时间范围
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        DateTimeOffset start = TryParseDateTime(startTime) ?? now;
                        DateTimeOffset end   = TryParseDateTime(endTime)   ?? now.AddDays(7);

                        if (end <= start)
                        {
                            return (object)new { success = false, error = "结束时间必须晚于开始时间。" };
                        }

                        // 5. 限制 pageSize
                        int clampedPageSize = Math.Clamp(pageSize, 1, 100);

                        // 6. 获取 Tenant Access Token
                        string? tenantToken = await GetTenantAccessTokenAsync(settings, logger, ct);
                        if (string.IsNullOrWhiteSpace(tenantToken))
                        {
                            return (object)new { success = false, error = "获取飞书 Tenant Access Token 失败，请检查渠道 AppId/AppSecret 配置。" };
                        }

                        // 7. 查询日历事件
                        var events = await QueryCalendarEventsAsync(
                            settings.ApiBaseUrl, calendarId, tenantToken,
                            start, end, clampedPageSize, logger, ct);

                        if (events is null)
                        {
                            return (object)new { success = false, error = "查询飞书日历失败，请确认 Calendar ID 正确且机器人应用已申请 calendar:calendar.event:readonly 权限。" };
                        }

                        logger.LogInformation(
                            "get_feishu_calendar 成功 calendarId={CalendarId} eventCount={EventCount}",
                            calendarId, events.Count);

                        return (object)new
                        {
                            success = true,
                            calendarId,
                            startTime = start.ToString("o"),
                            endTime   = end.ToString("o"),
                            totalCount = events.Count,
                            events,
                        };
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "get_feishu_calendar 执行失败");
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "get_feishu_calendar",
                description: "查询飞书日历中的事件列表，支持指定时间范围（ISO 8601 格式）和最大返回条数。可传入日历 URL 或 Calendar ID。"),

            // ── create_feishu_event ──────────────────────────────────────────────
            AIFunctionFactory.Create(
                async (
                    [Description("日历 URL 或 Calendar ID（格式同 get_feishu_calendar 说明）")] string calendarIdOrUrl,
                    [Description("事件标题（必填），最长 1000 个字符")] string title,
                    [Description("开始时间，ISO 8601 格式（如 2024-01-15T14:00:00+08:00）；全天事件时格式为 2024-01-15")] string startTime,
                    [Description("结束时间，ISO 8601 格式（如 2024-01-15T15:00:00+08:00）；全天事件时格式为 2024-01-15")] string endTime,
                    [Description("事件描述/备注（可选），支持纯文本")] string description = "",
                    [Description("事件地点（可选），纯文本字符串")] string location = "",
                    [Description("是否为全天事件，默认 false；为 true 时 startTime/endTime 只需传日期部分（yyyy-MM-dd）")] bool isAllDay = false,
                    CancellationToken ct = default) =>
                {
                    try
                    {
                        // 1. 解析 CalendarId
                        string calendarId = ExtractCalendarId(calendarIdOrUrl);
                        if (string.IsNullOrWhiteSpace(calendarId))
                        {
                            return (object)new { success = false, error = "无法解析日历 ID，请提供有效的飞书日历 URL 或 Calendar ID。" };
                        }

                        // 2. 安全校验
                        if (!IsValidCalendarId(calendarId))
                        {
                            return (object)new { success = false, error = "Calendar ID 格式不正确，只允许字母、数字、点、横线和 @ 符号。" };
                        }

                        // 3. 白名单校验
                        if (settings.AllowedCalendarIds.Length > 0 &&
                            !settings.AllowedCalendarIds.Contains(calendarId, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该 Calendar ID 不在渠道允许的白名单内，Agent 无权向此日历写入事件。" };
                        }

                        // 4. 标题非空
                        if (string.IsNullOrWhiteSpace(title))
                        {
                            return (object)new { success = false, error = "事件标题不能为空。" };
                        }

                        // 5. 时间参数校验
                        if (string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime))
                        {
                            return (object)new { success = false, error = "开始时间和结束时间均为必填项。" };
                        }

                        // 6. 获取 Tenant Access Token
                        string? tenantToken = await GetTenantAccessTokenAsync(settings, logger, ct);
                        if (string.IsNullOrWhiteSpace(tenantToken))
                        {
                            return (object)new { success = false, error = "获取飞书 Tenant Access Token 失败，请检查渠道 AppId/AppSecret 配置。" };
                        }

                        // 7. 创建日历事件
                        string? eventId = await CreateCalendarEventAsync(
                            settings.ApiBaseUrl, calendarId, tenantToken,
                            title, startTime, endTime, description, location, isAllDay, logger, ct);

                        if (eventId is null)
                        {
                            return (object)new { success = false, error = "创建飞书日历事件失败，请确认 Calendar ID 正确且机器人应用已申请 calendar:calendar.event:write 权限。" };
                        }

                        logger.LogInformation(
                            "create_feishu_event 成功 calendarId={CalendarId} eventId={EventId} title={Title}",
                            calendarId, eventId, title);

                        return (object)new
                        {
                            success = true,
                            calendarId,
                            eventId,
                            title,
                            startTime,
                            endTime,
                            isAllDay,
                        };
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "create_feishu_event 执行失败");
                        return (object)new { success = false, error = ex.Message };
                    }
                },
                name: "create_feishu_event",
                description: "在飞书日历中创建新事件（会议/提醒）。支持指定标题、开始/结束时间、描述、地点及是否全天事件。"),
        ];
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 私有辅助方法
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 从飞书日历 URL 中提取 Calendar ID，或直接返回输入（若非 HTTP URL）。
    /// <para>URL 示例：https://xxxx.feishu.cn/calendar/general?calendarId=XXX</para>
    /// <para>也支持 applink 格式：https://applink.feishu.cn/client/calendar/event?eventid=XXX</para>
    /// </summary>
    internal static string ExtractCalendarId(string input)
    {
        input = input.Trim();

        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(input);

                // 尝试从查询参数 calendarId / calenderId 中取值
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                string? fromQuery = query["calendarId"] ?? query["calenderId"] ?? query["calendar_id"];
                if (!string.IsNullOrWhiteSpace(fromQuery))
                    return fromQuery.Trim();

                // 尝试从路径末段提取（/calendar/XXXX 或 /calendar/general/XXXX）
                string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                int calIdx = Array.FindLastIndex(segments, s =>
                    s.Equals("calendar", StringComparison.OrdinalIgnoreCase));
                if (calIdx >= 0 && calIdx + 1 < segments.Length)
                {
                    string candidate = segments[calIdx + 1];
                    // 跳过 "general" 这类非 ID 路径段
                    if (!candidate.Equals("general", StringComparison.OrdinalIgnoreCase) &&
                        !candidate.Equals("event", StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // URL 解析失败，回退直接使用原始输入
            }

            return string.Empty;
        }

        // 非 URL：直接当作 Calendar ID 使用
        return input;
    }

    /// <summary>
    /// 校验 Calendar ID 格式，只允许字母、数字、点、横线和 @ 符号，
    /// 防止路径注入到 REST API URL。
    /// </summary>
    internal static bool IsValidCalendarId(string calendarId) =>
        !string.IsNullOrWhiteSpace(calendarId) &&
        calendarId.Length <= 256 &&
        System.Text.RegularExpressions.Regex.IsMatch(calendarId, @"^[a-zA-Z0-9.\-@_]+$");

    /// <summary>尝试解析 ISO 8601 日期时间字符串，失败返回 null。</summary>
    private static DateTimeOffset? TryParseDateTime(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (DateTimeOffset.TryParse(input, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dto))
            return dto;
        return null;
    }

    /// <summary>
    /// 获取飞书 Tenant Access Token（与 Doc/Bitable/Wiki 工具相同逻辑）。
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
    /// 查询飞书日历事件列表。
    /// API: GET /open-apis/calendar/v4/calendars/{calendar_id}/events
    /// </summary>
    private static async Task<List<object>?> QueryCalendarEventsAsync(
        string apiBaseUrl,
        string calendarId,
        string tenantToken,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int pageSize,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.BaseAddress = new Uri(apiBaseUrl);
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {tenantToken}");

            long startTs = startTime.ToUnixTimeSeconds();
            long endTs   = endTime.ToUnixTimeSeconds();
            string encodedId = Uri.EscapeDataString(calendarId);
            string url = $"/open-apis/calendar/v4/calendars/{encodedId}/events" +
                         $"?start_time={startTs}&end_time={endTs}&page_size={pageSize}";

            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "get_feishu_calendar API 返回 {StatusCode}", (int)response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            int code = doc.RootElement.TryGetProperty("code", out var codeEl)
                ? codeEl.GetInt32() : -1;
            if (code != 0)
            {
                string msg = doc.RootElement.TryGetProperty("msg", out var msgEl)
                    ? msgEl.GetString() ?? string.Empty : string.Empty;
                logger.LogWarning("get_feishu_calendar API code={Code} msg={Msg}", code, msg);
                return null;
            }

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("items", out var items))
            {
                return [];
            }

            var result = new List<object>();
            foreach (var item in items.EnumerateArray())
            {
                string? eventId  = item.TryGetProperty("event_id",    out var eid) ? eid.GetString()  : null;
                string? summary  = item.TryGetProperty("summary",      out var sum) ? sum.GetString()  : null;
                string? desc     = item.TryGetProperty("description",  out var ds)  ? ds.GetString()   : null;
                string? loc      = item.TryGetProperty("location",     out var lc)  ? ExtractLocationName(lc) : null;
                string? sTime    = item.TryGetProperty("start_time",   out var st)  ? ExtractEventTime(st)  : null;
                string? eTime    = item.TryGetProperty("end_time",     out var et)  ? ExtractEventTime(et)  : null;
                bool    allDay   = item.TryGetProperty("is_all_day",   out var iad) && iad.GetBoolean();
                string? status   = item.TryGetProperty("status",       out var sta) ? sta.GetString()  : null;

                result.Add(new
                {
                    eventId,
                    summary,
                    description = desc,
                    location = loc,
                    startTime = sTime,
                    endTime   = eTime,
                    isAllDay  = allDay,
                    status,
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "QueryCalendarEventsAsync 发生异常");
            return null;
        }
    }

    /// <summary>
    /// 在指定日历中创建事件。
    /// API: POST /open-apis/calendar/v4/calendars/{calendar_id}/events
    /// </summary>
    private static async Task<string?> CreateCalendarEventAsync(
        string apiBaseUrl,
        string calendarId,
        string tenantToken,
        string title,
        string startTime,
        string endTime,
        string description,
        string location,
        bool isAllDay,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.BaseAddress = new Uri(apiBaseUrl);
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {tenantToken}");

            // 根据是否全天事件选择不同的时间对象格式
            object startObj = isAllDay
                ? (object)new { date = startTime }
                : new { timestamp = ToUnixTimestampString(startTime), timezone = "Asia/Shanghai" };

            object endObj = isAllDay
                ? (object)new { date = endTime }
                : new { timestamp = ToUnixTimestampString(endTime), timezone = "Asia/Shanghai" };

            var bodyObj = new Dictionary<string, object>
            {
                ["summary"]    = title,
                ["start_time"] = startObj,
                ["end_time"]   = endObj,
                ["is_all_day"] = isAllDay,
            };

            if (!string.IsNullOrWhiteSpace(description))
                bodyObj["description"] = description;

            if (!string.IsNullOrWhiteSpace(location))
                bodyObj["location"] = new { name = location };

            string bodyJson = JsonSerializer.Serialize(bodyObj);
            using var httpContent = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            string encodedId = Uri.EscapeDataString(calendarId);
            using var response = await http.PostAsync(
                $"/open-apis/calendar/v4/calendars/{encodedId}/events", httpContent, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "create_feishu_event API 返回 {StatusCode}", (int)response.StatusCode);
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            int code = doc.RootElement.TryGetProperty("code", out var codeEl)
                ? codeEl.GetInt32() : -1;
            if (code != 0)
            {
                string msg = doc.RootElement.TryGetProperty("msg", out var msgEl)
                    ? msgEl.GetString() ?? string.Empty : string.Empty;
                logger.LogWarning("create_feishu_event API code={Code} msg={Msg}", code, msg);
                return null;
            }

            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("event", out var ev) &&
                ev.TryGetProperty("event_id", out var eid))
            {
                return eid.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CreateCalendarEventAsync 发生异常");
            return null;
        }
    }

    /// <summary>将日期时间字符串转为飞书 API 所需的 Unix 时间戳字符串（秒级）。</summary>
    private static string ToUnixTimestampString(string isoDatetime)
    {
        if (DateTimeOffset.TryParse(isoDatetime, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dto))
            return dto.ToUnixTimeSeconds().ToString();

        // 无法解析时原样返回，让 API 给出错误提示
        return isoDatetime;
    }

    /// <summary>从日历事件时间对象中提取可读时间字符串。</summary>
    private static string? ExtractEventTime(JsonElement timeEl)
    {
        // 全天事件返回 date 字段；普通事件返回 timestamp（Unix 秒）
        if (timeEl.TryGetProperty("date", out var dateEl))
            return dateEl.GetString();

        if (timeEl.TryGetProperty("timestamp", out var tsEl))
        {
            string? tsStr = tsEl.GetString();
            if (long.TryParse(tsStr, out long ts))
            {
                return DateTimeOffset.FromUnixTimeSeconds(ts)
                    .ToOffset(TimeSpan.FromHours(8))
                    .ToString("yyyy-MM-ddTHH:mm:sszzz");
            }
        }

        return null;
    }

    /// <summary>从 location 对象中提取地点名称。</summary>
    private static string? ExtractLocationName(JsonElement locationEl)
    {
        if (locationEl.ValueKind == JsonValueKind.String)
            return locationEl.GetString();

        if (locationEl.ValueKind == JsonValueKind.Object &&
            locationEl.TryGetProperty("name", out var nameEl))
            return nameEl.GetString();

        return null;
    }
}
