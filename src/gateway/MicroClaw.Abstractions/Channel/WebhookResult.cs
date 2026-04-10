namespace MicroClaw.Abstractions.Channel;

/// <summary>渠道 Webhook 处理结果。封装响应体和 HTTP 状态码，供端点层返回正确的 HTTP 响应。</summary>
public sealed record WebhookResult(string? Body, int StatusCode = 200)
{
    /// <summary>处理成功，无需响应体。</summary>
    public static readonly WebhookResult Empty = new(null, 200);

    /// <summary>签名/时间戳验证失败。</summary>
    public static WebhookResult Unauthorized(string message)
        => new(System.Text.Json.JsonSerializer.Serialize(new { success = false, message }), 401);

    /// <summary>成功，携带响应体（如 URL 验证 challenge）。</summary>
    public static WebhookResult Ok(string body) => new(body, 200);
}
