using MicroClaw.Abstractions.Sessions;
namespace MicroClaw.Endpoints.Session;

/// <summary>
/// GET /api/sessions — 获取顶层会话（子代理会话不对外暴露）
/// </summary>
internal sealed class GetSessionsEndpoint : IEndpointDefinition
{
    public string Route => "/sessions";
    public string[] Methods => new[] { HttpMethods.Get };
    public string[] Tags => new[] { "Sessions" };
    public Delegate Handler => ExecuteAsync;
    private static IResult ExecuteAsync([AsParameters]EndpointContext context)
    {
        ISessionService service = context.Engine.GetRequiredService<ISessionService>();
        return Results.Ok(service.GetAll().Select(s => s.ToInfo()).ToList());
    }
    
}