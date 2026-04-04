using MicroClaw.Pet;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Prompt;
using MicroClaw.Pet.Rag;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.Storage;
using MicroClaw.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Endpoints;

/// <summary>
/// Pet 管理 REST API 端点。
/// </summary>
public static class PetEndpoints
{
    public static IEndpointRouteBuilder MapPetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── GET /api/sessions/{id}/pet — 获取 Pet 状态 + 情绪 ─────────────

        endpoints.MapGet("/sessions/{id}/pet", async (
            string id,
            SessionStore sessionStore,
            PetStateStore stateStore,
            IEmotionStore emotionStore,
            PetRateLimiter rateLimiter,
            CancellationToken ct) =>
        {
            if (sessionStore.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            PetState? state = await stateStore.LoadAsync(id, ct);
            if (state is null)
                return Results.NotFound(new { success = false, message = $"Pet not found for session '{id}'.", errorCode = "NOT_FOUND" });

            EmotionState emotion = await emotionStore.GetCurrentAsync(id, ct);
            RateLimitStatus? rateLimit = await rateLimiter.GetStatusAsync(id, ct);
            PetConfig config = await stateStore.LoadConfigAsync(id, ct) ?? new PetConfig();

            return Results.Ok(new PetStatusDto(
                SessionId: id,
                BehaviorState: state.BehaviorState.ToString(),
                Emotion: new EmotionDto(emotion.Alertness, emotion.Mood, emotion.Curiosity, emotion.Confidence),
                Enabled: config.Enabled,
                RateLimit: rateLimit is not null
                    ? new RateLimitDto(rateLimit.MaxCalls, rateLimit.UsedCalls, rateLimit.RemainingCalls, rateLimit.IsExhausted)
                    : null,
                LastHeartbeatAt: state.LastHeartbeatAt,
                CreatedAt: state.CreatedAt,
                UpdatedAt: state.UpdatedAt));
        })
        .WithTags("Pet");

        // ── POST /api/sessions/{id}/pet/config — 更新 Pet 配置 ───────────

        endpoints.MapPost("/sessions/{id}/pet/config", async (
            string id,
            UpdatePetConfigRequest req,
            SessionStore sessionStore,
            PetStateStore stateStore,
            CancellationToken ct) =>
        {
            if (sessionStore.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            PetState? state = await stateStore.LoadAsync(id, ct);
            if (state is null)
                return Results.NotFound(new { success = false, message = $"Pet not found for session '{id}'.", errorCode = "NOT_FOUND" });

            PetConfig config = await stateStore.LoadConfigAsync(id, ct) ?? new PetConfig();

            // 合并更新：仅覆盖请求中提供的字段
            if (req.Enabled.HasValue) config.Enabled = req.Enabled.Value;
            if (req.ActiveHoursStart.HasValue) config.ActiveHoursStart = req.ActiveHoursStart.Value;
            if (req.ActiveHoursEnd.HasValue) config.ActiveHoursEnd = req.ActiveHoursEnd.Value;
            if (req.MaxLlmCallsPerWindow.HasValue) config.MaxLlmCallsPerWindow = req.MaxLlmCallsPerWindow.Value;
            if (req.WindowHours.HasValue) config.WindowHours = req.WindowHours.Value;
            if (req.AllowedAgentIds is not null) config.AllowedAgentIds = req.AllowedAgentIds;
            if (req.PreferredProviderId is not null) config.PreferredProviderId = req.PreferredProviderId == "" ? null : req.PreferredProviderId;
            if (req.SocialMode.HasValue) config.SocialMode = req.SocialMode.Value;

            await stateStore.SaveConfigAsync(id, config, ct);
            return Results.Ok(config);
        })
        .WithTags("Pet");

        // ── GET /api/sessions/{id}/pet/journal — 行为日志 ────────────────

        endpoints.MapGet("/sessions/{id}/pet/journal", async (
            string id,
            SessionStore sessionStore,
            PetStateStore stateStore,
            int? limit,
            CancellationToken ct) =>
        {
            if (sessionStore.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            PetState? state = await stateStore.LoadAsync(id, ct);
            if (state is null)
                return Results.NotFound(new { success = false, message = $"Pet not found for session '{id}'.", errorCode = "NOT_FOUND" });

            int maxLines = Math.Clamp(limit ?? 100, 1, 500);
            IReadOnlyList<string> lines = await stateStore.ReadJournalAsync(id, maxLines, ct);
            return Results.Ok(new { entries = lines, count = lines.Count });
        })
        .WithTags("Pet");

        // ── GET /api/sessions/{id}/pet/knowledge — RAG 概要 ──────────────

        endpoints.MapGet("/sessions/{id}/pet/knowledge", async (
            string id,
            SessionStore sessionStore,
            PetStateStore stateStore,
            PetRagScope ragScope,
            CancellationToken ct) =>
        {
            if (sessionStore.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            PetState? state = await stateStore.LoadAsync(id, ct);
            if (state is null)
                return Results.NotFound(new { success = false, message = $"Pet not found for session '{id}'.", errorCode = "NOT_FOUND" });

            int chunkCount = await ragScope.GetChunkCountAsync(id, ct);
            string dbPath = ragScope.GetDatabasePath(id);
            long dbSizeBytes = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0;

            return Results.Ok(new PetKnowledgeDto(chunkCount, dbSizeBytes));
        })
        .WithTags("Pet");

        // ── GET /api/sessions/{id}/pet/prompts — 查看提示词 ──────────────

        endpoints.MapGet("/sessions/{id}/pet/prompts", async (
            string id,
            SessionStore sessionStore,
            PetStateStore stateStore,
            PetPromptStore promptStore,
            CancellationToken ct) =>
        {
            if (sessionStore.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            PetState? state = await stateStore.LoadAsync(id, ct);
            if (state is null)
                return Results.NotFound(new { success = false, message = $"Pet not found for session '{id}'.", errorCode = "NOT_FOUND" });

            var personality = await promptStore.LoadPersonalityAsync(id, ct);
            var dispatchRules = await promptStore.LoadDispatchRulesAsync(id, ct);
            var knowledgeInterests = await promptStore.LoadKnowledgeInterestsAsync(id, ct);

            return Results.Ok(new PetPromptsDto(
                Personality: new PersonalityDto(personality.Persona, personality.Tone, personality.Language),
                DispatchRules: new DispatchRulesDto(dispatchRules.DefaultStrategy,
                    dispatchRules.Rules.Select(r => new DispatchRuleDto(r.Pattern, r.PreferredModelType, r.Notes)).ToList()),
                KnowledgeInterests: new KnowledgeInterestsDto(
                    knowledgeInterests.Topics.Select(t => new KnowledgeTopicDto(t.Name, t.Description, t.Priority)).ToList())));
        })
        .WithTags("Pet");

        // ── POST /api/sessions/{id}/pet/prompts — 修改提示词 ─────────────

        endpoints.MapPost("/sessions/{id}/pet/prompts", async (
            string id,
            UpdatePetPromptsRequest req,
            SessionStore sessionStore,
            PetStateStore stateStore,
            PetPromptStore promptStore,
            CancellationToken ct) =>
        {
            if (sessionStore.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            PetState? state = await stateStore.LoadAsync(id, ct);
            if (state is null)
                return Results.NotFound(new { success = false, message = $"Pet not found for session '{id}'.", errorCode = "NOT_FOUND" });

            // 渐进式更新：仅覆盖请求中提供的部分
            if (req.Personality is not null)
            {
                var current = await promptStore.LoadPersonalityAsync(id, ct);
                if (req.Personality.Persona is not null) current.Persona = req.Personality.Persona;
                if (req.Personality.Tone is not null) current.Tone = req.Personality.Tone;
                if (req.Personality.Language is not null) current.Language = req.Personality.Language;
                await promptStore.SavePersonalityAsync(id, current, ct);
            }

            if (req.DispatchRules is not null)
            {
                var rules = new DispatchRules
                {
                    DefaultStrategy = req.DispatchRules.DefaultStrategy ?? "default",
                    Rules = req.DispatchRules.Rules?.Select(r => new DispatchRule
                    {
                        Pattern = r.Pattern,
                        PreferredModelType = r.PreferredModelType,
                        Notes = r.Notes ?? string.Empty,
                    }).ToList() ?? [],
                };
                await promptStore.SaveDispatchRulesAsync(id, rules, ct);
            }

            if (req.KnowledgeInterests is not null)
            {
                var interests = new KnowledgeInterests
                {
                    Topics = req.KnowledgeInterests.Topics?.Select(t => new KnowledgeTopic
                    {
                        Name = t.Name,
                        Description = t.Description,
                        Priority = t.Priority ?? "medium",
                    }).ToList() ?? [],
                };
                await promptStore.SaveKnowledgeInterestsAsync(id, interests, ct);
            }

            return Results.Ok(new { success = true });
        })
        .WithTags("Pet");

        // ── GET /api/sessions/{id}/pet/emotion/history — 情绪历史曲线 ────

        endpoints.MapGet("/sessions/{id}/pet/emotion/history", async (
            string id,
            long? from,
            long? to,
            SessionStore sessionStore,
            PetStateStore stateStore,
            IEmotionStore emotionStore,
            CancellationToken ct) =>
        {
            if (sessionStore.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            PetState? state = await stateStore.LoadAsync(id, ct);
            if (state is null)
                return Results.NotFound(new { success = false, message = $"Pet not found for session '{id}'.", errorCode = "NOT_FOUND" });

            long toMs = to ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long fromMs = from ?? toMs - (long)(7 * 24 * 60 * 60 * 1000);

            var snapshots = await emotionStore.GetHistoryAsync(id, fromMs, toMs, ct);
            var result = snapshots.Select(s => new EmotionSnapshotDto(
                new EmotionDto(s.State.Alertness, s.State.Mood, s.State.Curiosity, s.State.Confidence),
                s.RecordedAtMs)).ToList();

            return Results.Ok(result);
        })
        .WithTags("Pet");

        return endpoints;
    }

    // ── DTO 定义 ──────────────────────────────────────────────────────────

    public sealed record PetStatusDto(
        string SessionId,
        string BehaviorState,
        EmotionDto Emotion,
        bool Enabled,
        RateLimitDto? RateLimit,
        DateTimeOffset? LastHeartbeatAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record EmotionDto(int Alertness, int Mood, int Curiosity, int Confidence);

    public sealed record EmotionSnapshotDto(EmotionDto State, long RecordedAtMs);

    public sealed record RateLimitDto(int MaxCalls, int UsedCalls, int RemainingCalls, bool IsExhausted);

    public sealed record PetKnowledgeDto(int ChunkCount, long DbSizeBytes);

    public sealed record PetPromptsDto(
        PersonalityDto Personality,
        DispatchRulesDto DispatchRules,
        KnowledgeInterestsDto KnowledgeInterests);

    public sealed record PersonalityDto(string Persona, string Tone, string Language);
    public sealed record DispatchRulesDto(string DefaultStrategy, IReadOnlyList<DispatchRuleDto> Rules);
    public sealed record DispatchRuleDto(string Pattern, string PreferredModelType, string Notes);
    public sealed record KnowledgeInterestsDto(IReadOnlyList<KnowledgeTopicDto> Topics);
    public sealed record KnowledgeTopicDto(string Name, string Description, string Priority);

    // ── 请求模型 ──────────────────────────────────────────────────────────

    public sealed record UpdatePetConfigRequest(
        bool? Enabled = null,
        int? ActiveHoursStart = null,
        int? ActiveHoursEnd = null,
        int? MaxLlmCallsPerWindow = null,
        double? WindowHours = null,
        List<string>? AllowedAgentIds = null,
        string? PreferredProviderId = null,
        bool? SocialMode = null);

    public sealed record UpdatePetPromptsRequest(
        UpdatePersonalityRequest? Personality = null,
        UpdateDispatchRulesRequest? DispatchRules = null,
        UpdateKnowledgeInterestsRequest? KnowledgeInterests = null);

    public sealed record UpdatePersonalityRequest(
        string? Persona = null,
        string? Tone = null,
        string? Language = null);

    public sealed record UpdateDispatchRulesRequest(
        string? DefaultStrategy = null,
        List<UpdateDispatchRuleRequest>? Rules = null);

    public sealed record UpdateDispatchRuleRequest(
        string Pattern,
        string PreferredModelType,
        string? Notes = null);

    public sealed record UpdateKnowledgeInterestsRequest(
        List<UpdateKnowledgeTopicRequest>? Topics = null);

    public sealed record UpdateKnowledgeTopicRequest(
        string Name,
        string Description,
        string? Priority = null);
}
