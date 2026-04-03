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
		api.MapSandboxPublicEndpoints();

		// A2A 端点（公开访问，无需 JWT）
		endpoints.MapA2AEndpoints();

		var protectedApi = api.RequireAuthorization();
		protectedApi.MapConfigEndpoints();
		protectedApi.MapSystemEndpoints();
		protectedApi.MapChannelEndpoints();
		protectedApi.MapAdminEndpoints();
		protectedApi.MapSessionEndpoints();
		protectedApi.MapSandboxProtectedEndpoints();
		protectedApi.MapAgentEndpoints();
		protectedApi.MapFeishuDocImportEndpoints();
		protectedApi.MapCronEndpoints();
		protectedApi.MapSkillEndpoints();
		protectedApi.MapUsageEndpoints();
		protectedApi.MapMcpEndpoints();
		protectedApi.MapToolsEndpoints();
		protectedApi.MapWorkflowEndpoints();
		protectedApi.MapRagEndpoints();
		// P-B-6: EmotionEndpoints 已随 Emotion 项目内联到 Pet，待 P-C 阶段添加新的 Session 级情绪端点
		protectedApi.MapPainMemoryEndpoints();
		protectedApi.MapPluginEndpoints();
		protectedApi.MapMarketplaceEndpoints();

		return endpoints;
	}
}
