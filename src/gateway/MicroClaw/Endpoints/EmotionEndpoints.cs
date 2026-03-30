using MicroClaw.Emotion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Endpoints;

/// <summary>
/// 情绪曲线 REST API 端点。
/// </summary>
public static class EmotionEndpoints
{
    public static IEndpointRouteBuilder MapEmotionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── 获取当前情绪状态 ──────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/emotion/current", async (
            string id,
            IEmotionStore store,
            CancellationToken ct) =>
        {
            EmotionState state = await store.GetCurrentAsync(id, ct);
            return Results.Ok(new EmotionStateDto(
                state.Alertness,
                state.Mood,
                state.Curiosity,
                state.Confidence));
        })
        .WithTags("Agents");

        // ── 查询情绪历史曲线 ──────────────────────────────────────────────────

        endpoints.MapPost("/agents/{id}/emotion/history", async (
            string id,
            EmotionHistoryRequest req,
            IEmotionStore store,
            CancellationToken ct) =>
        {
            if (req.From > req.To)
                return Results.BadRequest(new
                {
                    success = false,
                    message = "'from' must be less than or equal to 'to'.",
                    errorCode = "BAD_REQUEST"
                });

            IReadOnlyList<EmotionSnapshot> snapshots = await store.GetHistoryAsync(id, req.From, req.To, ct);

            IEnumerable<EmotionSnapshotDto> dtos = snapshots.Select(s => new EmotionSnapshotDto(
                s.State.Alertness,
                s.State.Mood,
                s.State.Curiosity,
                s.State.Confidence,
                s.RecordedAtMs));

            return Results.Ok(dtos);
        })
        .WithTags("Agents");

        return endpoints;
    }

    // ── DTO 定义 ──────────────────────────────────────────────────────────────

    /// <summary>当前情绪状态响应 DTO。</summary>
    public sealed record EmotionStateDto(
        int Alertness,
        int Mood,
        int Curiosity,
        int Confidence);

    /// <summary>历史情绪快照响应 DTO（含时间戳）。</summary>
    public sealed record EmotionSnapshotDto(
        int Alertness,
        int Mood,
        int Curiosity,
        int Confidence,
        long RecordedAtMs);

    /// <summary>查询情绪历史请求体。</summary>
    public sealed record EmotionHistoryRequest(long From, long To);
}
