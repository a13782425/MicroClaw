using MicroClaw.Agent.Endpoints;

namespace MicroClaw.Endpoints;

public static class GatewayEndpointRouteBuilderExtensions
{
	public static IEndpointRouteBuilder MapGatewayEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var api = endpoints.MapGroup("/api");

		api.MapHealthEndpoints();
		api.MapAuthEndpoints();
		api.MapChannelWebhookEndpoints();

		var protectedApi = api.RequireAuthorization();
		protectedApi.MapSystemEndpoints();
		protectedApi.MapChannelEndpoints();
		protectedApi.MapAdminEndpoints();
		protectedApi.MapSessionEndpoints();
		protectedApi.MapAgentEndpoints();
		protectedApi.MapCronEndpoints();

		return endpoints;
	}
}
