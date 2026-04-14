namespace MicroClaw.Channels;

/// <summary>渠道 Webhook 公共工具方法，消除各渠道间的重复实现。</summary>
internal static class WebhookUtils
{
    /// <summary>
    /// 检查时间戳是否在容差范围内（防重放攻击）。
    /// </summary>
    /// <param name="timestamp">Unix 秒级时间戳字符串。</param>
    /// <param name="toleranceSeconds">容差秒数（0 表示不限制）。</param>
    public static bool IsTimestampFresh(string? timestamp, int toleranceSeconds)
    {
        if (toleranceSeconds <= 0) return true;
        if (!long.TryParse(timestamp, out long unixSeconds)) return false;

        DateTimeOffset requestTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        double diff = Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalSeconds);
        return diff <= toleranceSeconds;
    }

    /// <summary>
    /// 从 XML 字符串中提取指定标签的文本内容（简单实现，不依赖 XML 解析器）。
    /// </summary>
    public static string? ExtractXmlField(string xml, string tagName)
    {
        string open  = $"<{tagName}>";
        string close = $"</{tagName}>";
        int start = xml.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return null;
        start += open.Length;
        int end = xml.IndexOf(close, start, StringComparison.Ordinal);
        return end < 0 ? null : xml[start..end].Trim();
    }
}
