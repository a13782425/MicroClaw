using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using FeishuNetSdk;
using FeishuNetSdk.Calendar;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-8: 飞书日历工具 — 提供 <c>get_feishu_calendar</c> 和 <c>create_feishu_event</c> AIFunction，
/// Agent 可查询日历事件列表或创建新的会议/提醒事项。
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
    public static IReadOnlyList<AIFunction> CreateTools(FeishuChannelSettings settings, IFeishuTenantApi api, ILogger logger)
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
                        string calendarId = ExtractCalendarId(calendarIdOrUrl);
                        if (string.IsNullOrWhiteSpace(calendarId))
                            return (object)new { success = false, error = "无法解析日历 ID，请提供有效的飞书日历 URL 或 Calendar ID。" };

                        if (!IsValidCalendarId(calendarId))
                            return (object)new { success = false, error = "Calendar ID 格式不正确，只允许字母、数字、点、横线和 @ 符号。" };

                        if (settings.AllowedCalendarIds.Length > 0 &&
                            !settings.AllowedCalendarIds.Contains(calendarId, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该 Calendar ID 不在渠道允许的白名单内，Agent 无权访问此日历。" };
                        }

                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        DateTimeOffset start = TryParseDateTime(startTime) ?? now;
                        DateTimeOffset end   = TryParseDateTime(endTime)   ?? now.AddDays(7);

                        if (end <= start)
                            return (object)new { success = false, error = "结束时间必须晚于开始时间。" };

                        int clampedPageSize = Math.Clamp(pageSize, 1, 100);

                        var response = await api.GetCalendarV4CalendarsByCalendarIdEventsAsync(
                            calendarId,
                            page_size: clampedPageSize,
                            start_time: start.ToUnixTimeSeconds().ToString(),
                            end_time: end.ToUnixTimeSeconds().ToString());

                        if (response.Code != 0)
                        {
                            logger.LogWarning("get_feishu_calendar API 返回错误 calendarId={CalendarId}: {Msg}",
                                calendarId, response.Msg);
                            return (object)new { success = false, error = response.Msg ?? "查询飞书日历失败，请确认 Calendar ID 正确且机器人应用已申请 calendar:calendar.event:readonly 权限。" };
                        }

                        var items = response.Data?.Items ?? [];
                        var events = items.Select(ev => new
                        {
                            eventId = ev.EventId,
                            summary = ev.Summary,
                            description = ev.Description,
                            location = ev.Location?.Name,
                            startTime = FormatTimeInfo(ev.StartTime),
                            endTime = FormatTimeInfo(ev.EndTime),
                            status = ev.Status,
                        }).ToList();

                        logger.LogInformation(
                            "get_feishu_calendar 成功 calendarId={CalendarId} eventCount={EventCount}",
                            calendarId, events.Count);

                        return (object)new
                        {
                            success = true,
                            calendarId,
                            startTime = start.ToString("o"),
                            endTime = end.ToString("o"),
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
                        string calendarId = ExtractCalendarId(calendarIdOrUrl);
                        if (string.IsNullOrWhiteSpace(calendarId))
                            return (object)new { success = false, error = "无法解析日历 ID，请提供有效的飞书日历 URL 或 Calendar ID。" };

                        if (!IsValidCalendarId(calendarId))
                            return (object)new { success = false, error = "Calendar ID 格式不正确，只允许字母、数字、点、横线和 @ 符号。" };

                        if (settings.AllowedCalendarIds.Length > 0 &&
                            !settings.AllowedCalendarIds.Contains(calendarId, StringComparer.Ordinal))
                        {
                            return (object)new { success = false, error = "该 Calendar ID 不在渠道允许的白名单内，Agent 无权向此日历写入事件。" };
                        }

                        if (string.IsNullOrWhiteSpace(title))
                            return (object)new { success = false, error = "事件标题不能为空。" };

                        if (string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime))
                            return (object)new { success = false, error = "开始时间和结束时间均为必填项。" };

                        var bodyDto = new PostCalendarV4CalendarsByCalendarIdEventsBodyDto
                        {
                            Summary = title,
                            StartTime = BuildTimeInfo(startTime, isAllDay),
                            EndTime = BuildTimeInfo(endTime, isAllDay),
                        };

                        if (!string.IsNullOrWhiteSpace(description))
                            bodyDto.Description = description;

                        if (!string.IsNullOrWhiteSpace(location))
                            bodyDto.Location = new PostCalendarV4CalendarsByCalendarIdEventsBodyDto.EventLocation { Name = location };

                        var response = await api.PostCalendarV4CalendarsByCalendarIdEventsAsync(
                            calendarId, bodyDto);

                        if (response.Code != 0)
                        {
                            logger.LogWarning("create_feishu_event API 返回错误 calendarId={CalendarId}: {Msg}",
                                calendarId, response.Msg);
                            return (object)new { success = false, error = response.Msg ?? "创建飞书日历事件失败，请确认 Calendar ID 正确且机器人应用已申请 calendar:calendar.event:write 权限。" };
                        }

                        string? eventId = response.Data?.Event?.EventId;

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
    // Utility methods
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 从飞书日历 URL 中提取 Calendar ID，或直接返回输入（若非 HTTP URL）。
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

                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                string? fromQuery = query["calendarId"] ?? query["calenderId"] ?? query["calendar_id"];
                if (!string.IsNullOrWhiteSpace(fromQuery))
                    return fromQuery.Trim();

                string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                int calIdx = Array.FindLastIndex(segments, s =>
                    s.Equals("calendar", StringComparison.OrdinalIgnoreCase));
                if (calIdx >= 0 && calIdx + 1 < segments.Length)
                {
                    string candidate = segments[calIdx + 1];
                    if (!candidate.Equals("general", StringComparison.OrdinalIgnoreCase) &&
                        !candidate.Equals("event", StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // URL parse failure — fall through
            }

            return string.Empty;
        }

        return input;
    }

    /// <summary>
    /// 校验 Calendar ID 格式，只允许字母、数字、点、横线和 @ 符号。
    /// </summary>
    internal static bool IsValidCalendarId(string calendarId) =>
        !string.IsNullOrWhiteSpace(calendarId) &&
        calendarId.Length <= 256 &&
        Regex.IsMatch(calendarId, @"^[a-zA-Z0-9.\-@_]+$");

    /// <summary>尝试解析 ISO 8601 日期时间字符串，失败返回 null。</summary>
    private static DateTimeOffset? TryParseDateTime(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (DateTimeOffset.TryParse(input, null, DateTimeStyles.RoundtripKind, out var dto))
            return dto;
        return null;
    }

    /// <summary>Build SDK TimeInfo for event creation.</summary>
    private static PostCalendarV4CalendarsByCalendarIdEventsBodyDto.TimeInfo BuildTimeInfo(string timeStr, bool isAllDay)
    {
        if (isAllDay)
        {
            return new PostCalendarV4CalendarsByCalendarIdEventsBodyDto.TimeInfo { Date = timeStr };
        }

        string timestamp = timeStr;
        if (DateTimeOffset.TryParse(timeStr, null, DateTimeStyles.RoundtripKind, out var dto))
        {
            timestamp = dto.ToUnixTimeSeconds().ToString();
        }

        return new PostCalendarV4CalendarsByCalendarIdEventsBodyDto.TimeInfo
        {
            Timestamp = timestamp,
            Timezone = "Asia/Shanghai",
        };
    }

    /// <summary>Format a TimeInfo from calendar event response to a readable string.</summary>
    private static string? FormatTimeInfo(GetCalendarV4CalendarsByCalendarIdEventsResponseDto.CalendarEvent.TimeInfo? timeInfo)
    {
        if (timeInfo is null) return null;

        // All-day events use the Date field
        if (!string.IsNullOrEmpty(timeInfo.Date))
            return timeInfo.Date;

        // Timed events use Timestamp (Unix seconds)
        if (!string.IsNullOrEmpty(timeInfo.Timestamp) && long.TryParse(timeInfo.Timestamp, out long ts))
        {
            return DateTimeOffset.FromUnixTimeSeconds(ts)
                .ToOffset(TimeSpan.FromHours(8))
                .ToString("yyyy-MM-ddTHH:mm:sszzz");
        }

        return null;
    }
}
