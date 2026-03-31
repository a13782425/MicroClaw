using MicroClaw.Plugins;
using MicroClaw.Plugins.Marketplace;
using MicroClaw.Plugins.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Endpoints;

public static class PluginEndpoints
{
    public static IEndpointRouteBuilder MapPluginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/plugins").WithTags("Plugins");

        // GET /api/plugins — list all plugins
        group.MapGet("/", (IPluginRegistry registry) =>
        {
            IReadOnlyList<PluginInfo> plugins = registry.GetAll();
            var result = plugins.Select(p => new
            {
                p.Name,
                p.IsEnabled,
                p.Source,
                p.InstalledAt,
                Description = p.Manifest?.Description,
                Version = p.Manifest?.Version,
                Author = p.Manifest?.Author?.Name,
                SkillCount = p.SkillPaths.Count,
                AgentCount = p.AgentPaths.Count,
                HookCount = p.Hooks.Count,
                HasMcpConfig = p.McpConfigPath is not null
            });
            return Results.Ok(result);
        });

        // GET /api/plugins/{name} — get plugin detail
        group.MapGet("/{name}", (string name, IPluginRegistry registry) =>
        {
            PluginInfo? plugin = registry.GetByName(name);
            return plugin is null ? Results.NotFound() : Results.Ok(plugin);
        });

        // POST /api/plugins/install — install a plugin from git (auto-detects marketplace)
        group.MapPost("/install", async (
            InstallPluginRequest req,
            IPluginRegistry registry,
            IMarketplaceManager marketplaceManager,
            IEnumerable<IPluginMarketplace> adapters,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url))
                return Results.BadRequest("url is required");

            var source = PluginSource.Git(req.Url, req.Ref);

            // Clone once to temp dir for detection
            string repoName = GitHelper.ExtractRepoName(req.Url);
            string tempDir = Path.Combine(Path.GetTempPath(), $"microclaw-detect-{repoName}-{Guid.NewGuid():N}");
            bool tempDirConsumed = false;
            try
            {
                await GitHelper.CloneAsync(req.Url, req.Ref, tempDir, ct);

                // Check if it has a plugin manifest (plugin.json)
                bool hasPluginManifest =
                    File.Exists(Path.Combine(tempDir, ".claude-plugin", "plugin.json")) ||
                    File.Exists(Path.Combine(tempDir, "plugin.json"));

                bool isMarketplace = adapters.Any(a => a.CanHandle(tempDir));

                if (isMarketplace && !hasPluginManifest)
                {
                    // Pure marketplace — no plugin.json, only marketplace.json
                    MarketplaceInfo marketplace = await marketplaceManager.AddFromDirectoryAsync(tempDir, source, ct);
                    tempDirConsumed = true;
                    return Results.Ok(new { type = "marketplace", marketplace });
                }

                // Has plugin.json (possibly also marketplace.json) — install as plugin
                PluginInfo plugin = await registry.InstallFromDirectoryAsync(tempDir, source, ct);
                tempDirConsumed = true;
                return Results.Ok(new { type = "plugin", plugin });
            }
            finally
            {
                if (!tempDirConsumed && Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
                }
            }
        });

        // POST /api/plugins/{name}/enable
        group.MapPost("/{name}/enable", async (string name, IPluginRegistry registry, CancellationToken ct) =>
        {
            PluginInfo? plugin = registry.GetByName(name);
            if (plugin is null) return Results.NotFound();
            await registry.EnableAsync(name, ct);
            return Results.Ok();
        });

        // POST /api/plugins/{name}/disable
        group.MapPost("/{name}/disable", async (string name, IPluginRegistry registry, CancellationToken ct) =>
        {
            PluginInfo? plugin = registry.GetByName(name);
            if (plugin is null) return Results.NotFound();
            await registry.DisableAsync(name, ct);
            return Results.Ok();
        });

        // POST /api/plugins/{name}/update
        group.MapPost("/{name}/update", async (string name, IPluginRegistry registry, CancellationToken ct) =>
        {
            PluginInfo? plugin = registry.GetByName(name);
            if (plugin is null) return Results.NotFound();
            PluginInfo updated = await registry.UpdateAsync(name, ct);
            return Results.Ok(updated);
        });

        // DELETE /api/plugins/{name}
        group.MapDelete("/{name}", async (string name, IPluginRegistry registry, CancellationToken ct) =>
        {
            PluginInfo? plugin = registry.GetByName(name);
            if (plugin is null) return Results.NotFound();
            await registry.UninstallAsync(name, ct);
            return Results.Ok();
        });

        // POST /api/plugins/reload — reload all plugins
        group.MapPost("/reload", async (IPluginRegistry registry, CancellationToken ct) =>
        {
            await registry.ReloadAsync(ct);
            return Results.Ok();
        });

        return endpoints;
    }
}

public record InstallPluginRequest(string Url, string? Ref = null);
