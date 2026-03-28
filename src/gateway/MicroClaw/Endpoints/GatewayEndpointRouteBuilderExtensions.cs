using MicroClaw.Agent.A2A;
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

		// A2A 端点（公开访问，无需 JWT）
		endpoints.MapA2AEndpoints();

		var protectedApi = api.RequireAuthorization();
		protectedApi.MapConfigEndpoints();
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
		protectedApi.MapToolsEndpoints();
		protectedApi.MapWorkflowEndpoints();

		return endpoints;
	}
}
