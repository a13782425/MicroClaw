using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Services;

namespace MicroClaw.Endpoints;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/config", () =>
        {
            var agent = MicroClawConfig.Get<AgentsOptions>();
            var skills = MicroClawConfig.Get<SkillOptions>();
            var emotion = MicroClawConfig.Get<EmotionOptions>();

            return Results.Ok(new SystemConfigDto
            {
                Agent = new AgentConfigSection { SubAgentMaxDepth = agent.SubAgentMaxDepth },
                Skills = new SkillsConfigSection { AdditionalFolders = skills.AdditionalFolders },
                Emotion = EmotionConfigSection.FromOptions(emotion),
            });
        })
        .WithTags("Config");

        endpoints.MapPost("/config/agent", (AgentConfigSection req) =>
        {
            if (req.SubAgentMaxDepth < 1 || req.SubAgentMaxDepth > 10)
                return Results.BadRequest("sub_agent_max_depth 必须在 1–10 之间。");

            var current = MicroClawConfig.Get<AgentsOptions>();
            MicroClawConfig.Save(new AgentsOptions
            {
                SubAgentMaxDepth = req.SubAgentMaxDepth,
                Items = current.Items,
            });
            return Results.Ok(new { message = "已保存，需重启生效。" });
        })
        .WithTags("Config");

        endpoints.MapPost("/config/skills", (SkillsConfigSection req) =>
        {
            if (req.AdditionalFolders.Any(f => string.IsNullOrWhiteSpace(f)))
                return Results.BadRequest("文件夹路径不能为空。");

            var current = MicroClawConfig.Get<SkillOptions>();
            MicroClawConfig.Save(new SkillOptions
            {
                AllowCommandInjection = current.AllowCommandInjection,
                CatalogCharBudget = current.CatalogCharBudget,
                DefaultFolder = current.DefaultFolder,
                AdditionalFolders = [.. req.AdditionalFolders],
            });
            return Results.Ok(new { message = "已保存，需重启生效。" });
        })
        .WithTags("Config");

        endpoints.MapPost("/config/emotion", (EmotionConfigSection req) =>
        {
            if (req.CautiousAlertnessThreshold is < 0 or > 100 ||
                req.CautiousConfidenceThreshold is < 0 or > 100 ||
                req.ExploreMinCuriosity is < 0 or > 100 ||
                req.ExploreMinMood is < 0 or > 100 ||
                req.RestMaxAlertness is < 0 or > 100 ||
                req.RestMaxMood is < 0 or > 100)
                return Results.BadRequest("情绪阈值必须在 0–100 之间。");

            static bool InvalidFloat(float v) => v < 0f || v > 2f;
            if (InvalidFloat(req.Normal.Temperature) || InvalidFloat(req.Explore.Temperature) ||
                InvalidFloat(req.Cautious.Temperature) || InvalidFloat(req.Rest.Temperature))
                return Results.BadRequest("Temperature 必须在 0.0–2.0 之间。");

            static bool InvalidTopP(float v) => v <= 0f || v > 1f;
            if (InvalidTopP(req.Normal.TopP) || InvalidTopP(req.Explore.TopP) ||
                InvalidTopP(req.Cautious.TopP) || InvalidTopP(req.Rest.TopP))
                return Results.BadRequest("TopP 必须在 (0, 1] 之间。");

            static bool InvalidDelta(int? v) => v.HasValue && (v.Value < -100 || v.Value > 100);
            var allDeltas = new[] {
                req.DeltaMessageSuccess, req.DeltaMessageFailed,
                req.DeltaToolSuccess, req.DeltaToolError,
                req.DeltaUserSatisfied, req.DeltaUserDissatisfied,
                req.DeltaTaskCompleted, req.DeltaTaskFailed,
                req.DeltaPainHigh, req.DeltaPainCritical,
            };
            if (allDeltas.Any(d => InvalidDelta(d.Alertness) || InvalidDelta(d.Mood) ||
                                   InvalidDelta(d.Curiosity) || InvalidDelta(d.Confidence)))
                return Results.BadRequest("加减分值必须在 -100 到 100 之间。");

            MicroClawConfig.Save(req.ToOptions());
            return Results.Ok(new { message = "已保存，需重启生效。" });
        })
        .WithTags("Config");

        return endpoints;
    }
}
