using MicroClaw.Plugins.Marketplace;
using MicroClaw.Plugins.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Endpoints;

public static class MarketplaceEndpoints
{
    public static IEndpointRouteBuilder MapMarketplaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/marketplace").WithTags("Marketplace");

        // GET /api/marketplace — list all registered marketplaces
        group.MapGet("/", (IMarketplaceManager manager) =>
        {
            IReadOnlyList<MarketplaceInfo> marketplaces = manager.GetAll();
            return Results.Ok(marketplaces);
        });

        // GET /api/marketplace/{name} — get marketplace detail
        group.MapGet("/{name}", (string name, IMarketplaceManager manager) =>
        {
            MarketplaceInfo? info = manager.GetByName(name);
            return info is null ? Results.NotFound() : Results.Ok(info);
        });

        // POST /api/marketplace — register a new marketplace
        group.MapPost("/", async (AddMarketplaceRequest req, IMarketplaceManager manager, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Url))
                return Results.BadRequest("url is required");

            var source = PluginSource.Git(req.Url, req.Ref);
            MarketplaceInfo info = await manager.AddAsync(source, ct);
            return Results.Ok(info);
        });

        // DELETE /api/marketplace/{name} — remove a marketplace
        group.MapDelete("/{name}", async (string name, IMarketplaceManager manager, CancellationToken ct) =>
        {
            MarketplaceInfo? info = manager.GetByName(name);
            if (info is null) return Results.NotFound();
            await manager.RemoveAsync(name, ct);
            return Results.Ok();
        });

        // POST /api/marketplace/{name}/update — git pull the marketplace index
        group.MapPost("/{name}/update", async (string name, IMarketplaceManager manager, CancellationToken ct) =>
        {
            MarketplaceInfo? info = manager.GetByName(name);
            if (info is null) return Results.NotFound();
            MarketplaceInfo updated = await manager.UpdateAsync(name, ct);
            return Results.Ok(updated);
        });

        // GET /api/marketplace/{name}/plugins — list plugins in a marketplace
        group.MapGet("/{name}/plugins", async (string name, string? keyword, string? category, IMarketplaceManager manager, CancellationToken ct) =>
        {
            MarketplaceInfo? info = manager.GetByName(name);
            if (info is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(keyword) || !string.IsNullOrWhiteSpace(category))
            {
                // Search with filters across this specific marketplace
                IReadOnlyList<MarketplacePluginEntry> plugins = await manager.ListPluginsAsync(name, ct);
                IEnumerable<MarketplacePluginEntry> filtered = plugins;

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    filtered = filtered.Where(p =>
                        (p.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) ||
                        (p.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) ||
                        (p.Keywords?.Any(k => k.Contains(keyword, StringComparison.OrdinalIgnoreCase)) == true));
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    filtered = filtered.Where(p =>
                        string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
                }

                return Results.Ok(filtered.ToList());
            }

            IReadOnlyList<MarketplacePluginEntry> allPlugins = await manager.ListPluginsAsync(name, ct);
            return Results.Ok(allPlugins);
        });

        // GET /api/marketplace/{name}/plugins/{pluginName} — get plugin detail from marketplace
        group.MapGet("/{name}/plugins/{pluginName}", async (string name, string pluginName, IMarketplaceManager manager, CancellationToken ct) =>
        {
            MarketplaceInfo? info = manager.GetByName(name);
            if (info is null) return Results.NotFound();

            IReadOnlyList<MarketplacePluginEntry> plugins = await manager.ListPluginsAsync(name, ct);
            MarketplacePluginEntry? plugin = plugins.FirstOrDefault(p =>
                string.Equals(p.Name, pluginName, StringComparison.OrdinalIgnoreCase));

            return plugin is null ? Results.NotFound() : Results.Ok(plugin);
        });

        // POST /api/marketplace/{name}/plugins/{pluginName}/install — install a plugin from marketplace
        group.MapPost("/{name}/plugins/{pluginName}/install", async (string name, string pluginName, IMarketplaceManager manager, CancellationToken ct) =>
        {
            MarketplaceInfo? info = manager.GetByName(name);
            if (info is null) return Results.NotFound();

            PluginInfo installed = await manager.InstallPluginAsync(name, pluginName, ct);
            return Results.Ok(installed);
        });

        // GET /api/marketplace/search — search across all marketplaces
        group.MapGet("/search", async (string? keyword, string? category, IMarketplaceManager manager, CancellationToken ct) =>
        {
            IReadOnlyList<MarketplacePluginEntry> results = await manager.SearchPluginsAsync(keyword, category, ct);
            return Results.Ok(results);
        });

        return endpoints;
    }

    public sealed record AddMarketplaceRequest(string Url, string? Ref = null);
}
