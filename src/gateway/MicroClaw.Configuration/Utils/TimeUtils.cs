namespace MicroClaw.Utils;

/// <summary>
/// 全局时间基准工具类。
/// 所有数据库时间字段统一存储为相对于 BaseTime 的毫秒偏移（long）或天数偏移（int）。
/// API 层仍返回 ISO 8601 字符串，由 Store 层负责转换。
/// </summary>
public static class TimeUtils
{
    /// <summary>
    /// 全局基准时间：2026-01-01 00:00:00 UTC
    /// </summary>
    public static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>当前时刻相对于 BaseTime 的毫秒偏移</summary>
    public static long NowMs() => ToMs(DateTimeOffset.UtcNow);

    /// <summary>今天相对于 BaseTime 的天数偏移</summary>
    public static int TodayDay() => ToDay(DateTimeOffset.UtcNow);

    /// <summary>将 DateTimeOffset 转换为相对于 BaseTime 的毫秒偏移</summary>
    public static long ToMs(DateTimeOffset dt) => (long)(dt - BaseTime).TotalMilliseconds;

    /// <summary>将毫秒偏移还原为 DateTimeOffset</summary>
    public static DateTimeOffset FromMs(long ms) => BaseTime.AddMilliseconds(ms);

    /// <summary>将 DateTimeOffset 转换为相对于 BaseTime 的天数偏移（截断到日）</summary>
    public static int ToDay(DateTimeOffset dt) => (int)(dt.UtcDateTime.Date - BaseTime.UtcDateTime.Date).TotalDays;

    /// <summary>将 DateOnly 转换为相对于 BaseTime 的天数偏移</summary>
    public static int ToDay(DateOnly date) => date.DayNumber - DateOnly.FromDateTime(BaseTime.UtcDateTime).DayNumber;

    /// <summary>将天数偏移还原为 DateOnly</summary>
    public static DateOnly FromDay(int day) => DateOnly.FromDateTime(BaseTime.UtcDateTime).AddDays(day);
}
