using MicroClaw.Agent;
using MicroClaw.Channels.Feishu;
using MicroClaw.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Endpoints;

/// <summary>
/// 全局工具目录 REST API — 返回所有内置工具分组 + MCP Server 工具分组，供 ToolsPage 动态展示。
/// 不含 Skills（Skills 有独立管理页面，概念层次不同）。
/// </summary>
public static class ToolsEndpoints
{
    public static IEndpointRouteBuilder MapToolsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/tools", async (
            McpServerConfigStore mcpStore,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var groups = new List<object>();

            // ── 内置分组：cron ──────────────────────────────────────────────
            groups.Add(new
            {
                id        = "cron",
                name      = "定时任务",
                type      = "builtin",
                isEnabled = true,
                tools     = CronTools.GetToolDescriptions()
                              .Select(t => new { name = t.Name, description = t.Description, isEnabled = true })
                              .ToList()
            });

            // ── 内置分组：subagent ──────────────────────────────────────────
            groups.Add(new
            {
                id        = "subagent",
                name      = "子代理 & DNA",
                type      = "builtin",
                isEnabled = true,
                tools     = SubAgentTools.GetToolDescriptions()
                              .Select(t => new { name = t.Name, description = t.Description, isEnabled = true })
                              .ToList()
            });

            // ── 内置分组：feishu ────────────────────────────────────────────
            groups.Add(new
            {
                id        = "feishu",
                name      = "飞书",
                type      = "builtin",
                isEnabled = true,
                tools     = FeishuToolsFactory.GetToolDescriptions()
                              .Select(t => new { name = t.Name, description = t.Description, isEnabled = true })
                              .ToList()
            });

            // ── MCP Server 分组（并行连接，超时 10s，失败不中断整体）──────────
            if (mcpStore.All.Count > 0)
            {
                var mcpTasks = mcpStore.All.Select(async srv =>
                {
                    if (!srv.IsEnabled)
                    {
                        return (object)new
                        {
                            id        = srv.Id,
                            name      = srv.Name,
                            type      = "mcp",
                            isEnabled = false,
                            tools     = Array.Empty<object>()
                        };
                    }

                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(TimeSpan.FromSeconds(10));

                        var (tools, connections) = await ToolRegistry.LoadToolsAsync([srv], loggerFactory, cts.Token);
                        try
                        {
                            return (object)new
                            {
                                id        = srv.Id,
                                name      = srv.Name,
                                type      = "mcp",
                                isEnabled = true,
                                tools     = tools.Select(t => new
                                {
                                    name        = t.Name,
                                    description = t.Description,
                                    isEnabled   = true
                                }).ToList()
                            };
                        }
                        finally
                        {
                            foreach (IAsyncDisposable conn in connections)
                                await conn.DisposeAsync();
                        }
                    }
                    catch
                    {
                        // 连接失败或超时：仍然展示该 Server，但 tools 为空，前端可显示错误状态
                        return (object)new
                        {
                            id        = srv.Id,
                            name      = srv.Name,
                            type      = "mcp",
                            isEnabled = true,
                            loadError = true,
                            tools     = Array.Empty<object>()
                        };
                    }
                });

                var mcpGroups = await Task.WhenAll(mcpTasks);
                groups.AddRange(mcpGroups);
            }

            return Results.Ok(groups);
        })
        .WithTags("Tools");

        return endpoints;
    }
}
