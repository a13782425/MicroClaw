namespace MicroClaw.Abstractions.Channel;

/// <summary>渠道 Webhook 处理结果。封装响应体、HTTP 状态码和内容类型，供端点层返回正确的 HTTP 响应。</summary>
public sealed record WebhookResult(string? Body, int StatusCode = 200, string? ContentType = null)
{
    /// <summary>处理成功，无需响应体。</summary>
    public static readonly WebhookResult Empty = new(null, 200);

    /// <summary>签名/时间戳验证失败。</summary>
    public static WebhookResult Unauthorized(string message)
        => new(System.Text.Json.JsonSerializer.Serialize(new { success = false, message }), 401);

    /// <summary>成功，携带 JSON 响应体（如飞书 URL 验证）。</summary>
    public static WebhookResult Ok(string body) => new(body, 200, "application/json");

    /// <summary>成功，携带纯文本响应体（如企业微信/微信 echostr 回调）。</summary>
    public static WebhookResult OkText(string body) => new(body, 200, "text/plain");

    /// <summary>成功，携带 XML 响应体。</summary>
    public static WebhookResult OkXml(string body) => new(body, 200, "application/xml");
}
