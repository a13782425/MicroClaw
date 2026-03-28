using MicroClaw.Agent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Endpoints;

/// <summary>
/// 全局工具目录 REST API — 返回所有工具分组（内置 + 渠道 + MCP），供 ToolsPage 动态展示。
/// </summary>
public static class ToolsEndpoints
{
    public static IEndpointRouteBuilder MapToolsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/tools", async (
            ToolCollector toolCollector,
            CancellationToken ct) =>
        {
            IReadOnlyList<ToolGroupInfo> groups = await toolCollector.GetToolGroupsAsync(agent: null, ct);
            return Results.Ok(groups);
        })
        .WithTags("Tools");

        return endpoints;
    }
}
