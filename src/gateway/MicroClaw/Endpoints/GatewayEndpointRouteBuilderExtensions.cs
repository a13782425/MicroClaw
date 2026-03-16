namespace MicroClaw.Endpoints;

public static class GatewayEndpointRouteBuilderExtensions
{
	public static IEndpointRouteBuilder MapGatewayEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var api = endpoints.MapGroup("/api");

		api.MapHealthEndpoints();
		api.MapAuthEndpoints();

		var protectedApi = api.RequireAuthorization();
		protectedApi.MapSystemEndpoints();

		return endpoints;
	}
}
