using MicroClaw.Channels;
using MicroClaw.Providers;

namespace MicroClaw.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/providers", (ProviderConfigStore store) =>
        {
            IEnumerable<object> result = store.All.Select(p => new
            {
                p.Id,
                p.DisplayName,
                Protocol = SerializeProtocol(p.Protocol),
                p.BaseUrl,
                ApiKey = MaskApiKey(p.ApiKey),
                p.ModelName,
                p.IsEnabled,
                p.Capabilities
            });
            return Results.Ok(result);
        })
        .WithTags("Providers");

        endpoints.MapPost("/providers", (ProviderCreateRequest req, ProviderConfigStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.DisplayName))
                return Results.BadRequest("DisplayName is required.");
            if (string.IsNullOrWhiteSpace(req.ModelName))
                return Results.BadRequest("ModelName is required.");
            if (string.IsNullOrWhiteSpace(req.ApiKey))
                return Results.BadRequest("ApiKey is required.");

            ProviderConfig config = new()
            {
                DisplayName = req.DisplayName.Trim(),
                Protocol = ParseProtocol(req.Protocol),
                BaseUrl = string.IsNullOrWhiteSpace(req.BaseUrl) ? null : req.BaseUrl.Trim(),
                ApiKey = req.ApiKey.Trim(),
                ModelName = req.ModelName.Trim(),
                IsEnabled = req.IsEnabled,
                Capabilities = req.Capabilities ?? new()
            };

            ProviderConfig created = store.Add(config);
            return Results.Ok(new { created.Id });
        })
        .WithTags("Providers");

        endpoints.MapPost("/providers/update", (ProviderUpdateRequest req, ProviderConfigStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest("Id is required.");

            ProviderConfig incoming = new()
            {
                DisplayName = req.DisplayName?.Trim() ?? string.Empty,
                Protocol = ParseProtocol(req.Protocol),
                BaseUrl = string.IsNullOrWhiteSpace(req.BaseUrl) ? null : req.BaseUrl.Trim(),
                ApiKey = req.ApiKey?.Trim() ?? string.Empty,
                ModelName = req.ModelName?.Trim() ?? string.Empty,
                IsEnabled = req.IsEnabled,
                Capabilities = req.Capabilities ?? new()
            };

            ProviderConfig? updated = store.Update(req.Id, incoming);
            if (updated is null)
                return Results.NotFound($"Provider '{req.Id}' not found.");

            return Results.Ok(new { updated.Id });
        })
        .WithTags("Providers");

        endpoints.MapPost("/providers/delete", (ProviderDeleteRequest req, ProviderConfigStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest("Id is required.");

            bool deleted = store.Delete(req.Id);
            if (!deleted)
                return Results.NotFound($"Provider '{req.Id}' not found.");

            return Results.Ok();
        })
        .WithTags("Providers");

        endpoints.MapGet("/channels", (IEnumerable<IChannel> channels) =>
        {
            return Results.Ok(channels
                .Select(c => c.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        })
        .WithTags("System");

        return endpoints;
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return string.Empty;
        if (apiKey.Length <= 8) return "***";
        return apiKey[..4] + "***" + apiKey[^4..];
    }

    private static ProviderProtocol ParseProtocol(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "openai" => ProviderProtocol.OpenAI,
            // 历史兼容：openai-responses 静默降级
            "openai-responses" => ProviderProtocol.OpenAI,
            "anthropic" => ProviderProtocol.Anthropic,
            _ => ProviderProtocol.OpenAI
        };

    private static string SerializeProtocol(ProviderProtocol protocol) =>
        protocol switch
        {
            ProviderProtocol.OpenAI => "openai",
            ProviderProtocol.Anthropic => "anthropic",
            _ => "openai"
        };
}

public sealed record ProviderCreateRequest(
    string DisplayName,
    string Protocol,
    string? BaseUrl,
    string ApiKey,
    string ModelName,
    bool IsEnabled = true,
    ProviderCapabilities? Capabilities = null);

public sealed record ProviderUpdateRequest(
    string Id,
    string? DisplayName,
    string? Protocol,
    string? BaseUrl,
    string? ApiKey,
    string? ModelName,
    bool IsEnabled = true,
    ProviderCapabilities? Capabilities = null);

public sealed record ProviderDeleteRequest(string Id);
