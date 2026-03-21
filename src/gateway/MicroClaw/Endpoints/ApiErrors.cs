namespace MicroClaw.Endpoints;

/// <summary>
/// 标准化 API 错误响应工厂。
/// 统一返回格式：{ success: false, message: "...", errorCode: "..." }
/// </summary>
internal static class ApiErrors
{
    public static IResult BadRequest(string message, string errorCode = "BAD_REQUEST") =>
        Results.BadRequest(new { success = false, message, errorCode });

    public static IResult NotFound(string message, string errorCode = "NOT_FOUND") =>
        Results.NotFound(new { success = false, message, errorCode });
}
