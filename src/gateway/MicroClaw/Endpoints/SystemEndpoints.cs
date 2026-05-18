using System.Text.Json;
using MicroClaw.Configuration.Options;
using MicroClaw.Providers;

namespace MicroClaw.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/providers", (ProviderService store) =>
        {
            IEnumerable<object> result = store.All.Select(p => new
            {
                p.Id,
                p.DisplayName,
                p.Protocol,
                p.ModelType,
                p.BaseUrl,
                ApiKey = MaskApiKey(p.ApiKey),
                p.ModelName,
                p.MaxOutputTokens,
                p.IsEnabled,
                p.IsDefault,
                Capabilities = ProviderUtils.DeserializeCapabilities(p.CapabilitiesJson)
            });
            return Results.Ok(result);
        })
        .WithTags("Providers");

        endpoints.MapPost("/providers", (ProviderCreateRequest req, ProviderService store) =>
        {
            if (string.IsNullOrWhiteSpace(req.DisplayName))
                return EndpointErrors.BadRequest("DisplayName is required.");
            if (string.IsNullOrWhiteSpace(req.ModelName))
                return EndpointErrors.BadRequest("ModelName is required.");
            if (string.IsNullOrWhiteSpace(req.ApiKey))
                return EndpointErrors.BadRequest("ApiKey is required.");

            ProviderEntityConfig entity = new()
            {
                Id = string.Empty,
                DisplayName = req.DisplayName.Trim(),
                Protocol = SerializeProtocol(ParseProtocol(req.Protocol)),
                ModelType = SerializeModelType(ParseModelType(req.ModelType)),
                BaseUrl = string.IsNullOrWhiteSpace(req.BaseUrl) ? string.Empty : req.BaseUrl.Trim(),
                ApiKey = req.ApiKey.Trim(),
                ModelName = req.ModelName.Trim(),
                MaxOutputTokens = req.MaxOutputTokens,
                IsEnabled = req.IsEnabled,
                IsDefault = false,
                CapabilitiesJson = JsonSerializer.Serialize(req.Capabilities ?? new ProviderCapabilities()),
            };

            ProviderEntityConfig created = store.Add(entity);
            return Results.Ok(new { created.Id });
        })
        .WithTags("Providers");

        endpoints.MapPost("/providers/update", (ProviderUpdateRequest req, ProviderService store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return EndpointErrors.BadRequest("Id is required.");

            ProviderEntityConfig? existing = store.GetById(req.Id);
            if (existing is null)
                return EndpointErrors.NotFound($"Provider '{req.Id}' not found.");

            ProviderEntityConfig incoming = existing with
            {
                DisplayName = req.DisplayName?.Trim() ?? existing.DisplayName,
                Protocol = req.Protocol != null ? SerializeProtocol(ParseProtocol(req.Protocol)) : existing.Protocol,
                ModelType = req.ModelType != null ? SerializeModelType(ParseModelType(req.ModelType)) : existing.ModelType,
                BaseUrl = req.BaseUrl != null ? (string.IsNullOrWhiteSpace(req.BaseUrl) ? string.Empty : req.BaseUrl.Trim()) : existing.BaseUrl,
                ApiKey = req.ApiKey?.Trim() ?? string.Empty,
                ModelName = req.ModelName?.Trim() ?? existing.ModelName,
                MaxOutputTokens = req.MaxOutputTokens ?? existing.MaxOutputTokens,
                IsEnabled = req.IsEnabled,
                CapabilitiesJson = req.Capabilities != null ? JsonSerializer.Serialize(req.Capabilities) : existing.CapabilitiesJson,
            };

            ProviderEntityConfig? updated = store.Update(req.Id, incoming);
            if (updated is null)
                return EndpointErrors.NotFound($"Provider '{req.Id}' not found.");

            return Results.Ok(new { updated.Id });
        })
        .WithTags("Providers");

        endpoints.MapPost("/providers/delete", (ProviderDeleteRequest req, ProviderService store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return EndpointErrors.BadRequest("Id is required.");

            bool deleted = store.Delete(req.Id);
            if (!deleted)
                return EndpointErrors.NotFound($"Provider '{req.Id}' not found.");

            return Results.Ok();
        })
        .WithTags("Providers");

        endpoints.MapPost("/providers/set-default", (ProviderSetDefaultRequest req, ProviderService store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return EndpointErrors.BadRequest("Id is required.");

            bool ok = store.SetDefault(req.Id);
            if (!ok)
                return EndpointErrors.NotFound($"Provider '{req.Id}' not found.");

            return Results.Ok();
        })
        .WithTags("Providers");

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

    private static ModelType ParseModelType(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "embedding" => ModelType.Embedding,
            _ => ModelType.Chat
        };

    private static string SerializeModelType(ModelType modelType) =>
        modelType switch
        {
            ModelType.Embedding => "embedding",
            _ => "chat"
        };
}

public sealed record ProviderCreateRequest(
    string DisplayName,
    string Protocol,
    string? BaseUrl,
    string ApiKey,
    string ModelName,
    int MaxOutputTokens = 8192,
    bool IsEnabled = true,
    ProviderCapabilities? Capabilities = null,
    string? ModelType = "chat");

public sealed record ProviderUpdateRequest(
    string Id,
    string? DisplayName,
    string? Protocol,
    string? BaseUrl,
    string? ApiKey,
    string? ModelName,
    int? MaxOutputTokens = null,
    bool IsEnabled = true,
    ProviderCapabilities? Capabilities = null,
    string? ModelType = null);

public sealed record ProviderDeleteRequest(string Id);

public sealed record ProviderSetDefaultRequest(string Id);
