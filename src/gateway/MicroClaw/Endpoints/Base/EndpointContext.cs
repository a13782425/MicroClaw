using System.Security.Claims;
using MicroClaw.Core;
using MicroClaw.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
namespace MicroClaw.Endpoints;
public sealed class EndpointContext
{
    [FromServices]
    public MicroEngine Engine { get; init; } = default!;
    [FromServices]
    public IHubContext<GatewayHub> Hub { get; init; } = default!;
    public HttpContext HttpContext { get; init; } = default!;
    
    public ClaimsPrincipal User => HttpContext.User;
    
    public CancellationToken CancellationToken => HttpContext.RequestAborted;
}