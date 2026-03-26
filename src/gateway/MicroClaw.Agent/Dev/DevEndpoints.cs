using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Agent.Dev;

/// <summary>
/// 开发调试端点（仅在 Development 环境注册）。
/// 暴露 Agent 执行指标、中间件限制参数和 Context Provider 列表，供前端 DevUI 展示。
/// </summary>
public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/dev");

        // GET /dev/metrics — 全量指标快照（工具耗时 + Agent 运行记录）
        group.MapGet("/metrics", (IDevMetricsService metrics) =>
            Results.Ok(metrics.GetSnapshot()))
            .WithName("GetDevMetrics")
            .WithTags("Dev");

        // GET /dev/middleware-limits — 各中间件配置上限
        group.MapGet("/middleware-limits", () => Results.Ok(new MiddlewareLimitsDto(
            new IterationsLimitDto(MaxIterationsMiddleware.MinIterations, MaxIterationsMiddleware.MaxIterations),
            new DepthLimitDto(MaxDepthMiddleware.DefaultMaxDepth))))
            .WithName("GetDevMiddlewareLimits")
            .WithTags("Dev");

        // GET /dev/context-providers — 已注册的 Context Provider 列表（名称 + Order）
        group.MapGet("/context-providers",
            (IEnumerable<IAgentContextProvider> providers) =>
                Results.Ok(providers
                    .OrderBy(p => p.Order)
                    .Select(p => new ContextProviderInfoDto(p.GetType().Name, p.Order))
                    .ToList()))
            .WithName("GetDevContextProviders")
            .WithTags("Dev");

        return endpoints;
    }
}

/// <summary>中间件限制参数 DTO。</summary>
public sealed record MiddlewareLimitsDto(
    IterationsLimitDto Iterations,
    DepthLimitDto MaxDepth);

public sealed record IterationsLimitDto(int Min, int Max);
public sealed record DepthLimitDto(int Default);

/// <summary>Context Provider 描述 DTO。</summary>
public sealed record ContextProviderInfoDto(string Name, int Order);
