namespace MicroClaw.Gateway.WebApi.Endpoints;

public static class GatewayEndpointRouteBuilderExtensions
{
	public static IEndpointRouteBuilder MapGatewayEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var api = endpoints.MapGroup("/api");

		api.MapHealthEndpoints();
		api.MapAuthEndpoints();
		api.MapSystemEndpoints();

		return endpoints;
	}
}
