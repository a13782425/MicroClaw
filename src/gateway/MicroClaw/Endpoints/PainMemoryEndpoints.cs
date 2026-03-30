using MicroClaw.Safety;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Endpoints;

/// <summary>
/// 痛觉记忆 REST API 端点。
/// </summary>
public static class PainMemoryEndpoints
{
    public static IEndpointRouteBuilder MapPainMemoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── 列出 Agent 所有痛觉记忆 ───────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/pain-memories", async (
            string id,
            IPainMemoryStore store,
            CancellationToken ct) =>
        {
            IReadOnlyList<PainMemory> memories = await store.GetAllAsync(id, ct);
            IEnumerable<PainMemoryDto> dtos = memories.Select(m => new PainMemoryDto(
                m.Id,
                m.AgentId,
                m.TriggerDescription,
                m.ConsequenceDescription,
                m.AvoidanceStrategy,
                m.Severity.ToString(),
                (int)m.Severity,
                m.OccurrenceCount,
                m.LastOccurredAtMs,
                m.CreatedAtMs));

            return Results.Ok(dtos);
        })
        .WithTags("Agents");

        // ── 删除指定痛觉记忆 ─────────────────────────────────────────────────

        endpoints.MapDelete("/agents/{id}/pain-memories/{memoryId}", async (
            string id,
            string memoryId,
            IPainMemoryStore store,
            CancellationToken ct) =>
        {
            await store.DeleteAsync(id, memoryId, ct);
            return Results.Ok(new { success = true });
        })
        .WithTags("Agents");

        return endpoints;
    }

    // ── DTO 定义 ──────────────────────────────────────────────────────────────

    /// <summary>痛觉记忆响应 DTO。</summary>
    public sealed record PainMemoryDto(
        string Id,
        string AgentId,
        string TriggerDescription,
        string ConsequenceDescription,
        string AvoidanceStrategy,
        string Severity,
        int SeverityLevel,
        int OccurrenceCount,
        long LastOccurredAtMs,
        long CreatedAtMs);
}
