using MicroClaw.Services;

namespace MicroClaw.Endpoints;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/config", (ConfigService svc) => Results.Ok(svc.GetSystemConfig()))
            .WithTags("Config");

        endpoints.MapPost("/config/agent", (AgentConfigSection req, ConfigService svc) =>
        {
            if (req.SubAgentMaxDepth < 1 || req.SubAgentMaxDepth > 10)
                return Results.BadRequest("sub_agent_max_depth 必须在 1–10 之间。");

            svc.UpdateAgentConfig(req);
            return Results.Ok(new { message = "已保存，需重启生效。" });
        })
        .WithTags("Config");

        endpoints.MapPost("/config/skills", (SkillsConfigSection req, ConfigService svc) =>
        {
            // 校验每个路径非空
            if (req.AdditionalFolders.Any(f => string.IsNullOrWhiteSpace(f)))
                return Results.BadRequest("文件夹路径不能为空。");

            svc.UpdateSkillsConfig(req);
            return Results.Ok(new { message = "已保存，需重启生效。" });
        })
        .WithTags("Config");

        return endpoints;
    }
}
