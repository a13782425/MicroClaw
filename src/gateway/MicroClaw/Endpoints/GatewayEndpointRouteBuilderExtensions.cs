using MicroClaw.Agent.Endpoints;
using MicroClaw.Skills.Endpoints;
using MicroClaw.Tools.Endpoints;

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
		protectedApi.MapFeishuDocImportEndpoints();
		protectedApi.MapCronEndpoints();
		protectedApi.MapSkillEndpoints();
		protectedApi.MapUsageEndpoints();
		protectedApi.MapMcpEndpoints();

		return endpoints;
	}
}
