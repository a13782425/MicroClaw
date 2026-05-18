using System.Text.Json.Serialization;
namespace MicroClaw.Endpoints;
/// <summary>
/// 标准化 API 错误响应工厂。
/// 统一返回格式：{ success: false, message: "...", errorCode: "..." }
/// </summary>
internal static class EndpointErrors
{
    public static IResult BadRequest(string message, string errorCode = "BAD_REQUEST") => Results.BadRequest(new EndpointErrorResponse(false, message, errorCode));
    
    public static IResult NotFound(string message, string errorCode = "NOT_FOUND") => Results.NotFound(new EndpointErrorResponse(false, message, errorCode));
}
/// <summary>
/// 标准化 API 错误响应类型。统一返回格式：{ success: false, message: "...", errorCode: "..." }
/// </summary>
/// <param name="Success"></param>
/// <param name="Message"></param>
/// <param name="ErrorCode"></param>
public sealed record EndpointErrorResponse(
    [property: JsonPropertyName("success")]
    bool Success,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("errorCode")]
    string ErrorCode);