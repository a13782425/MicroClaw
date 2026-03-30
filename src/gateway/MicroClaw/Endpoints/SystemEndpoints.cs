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
                ModelType = SerializeModelType(p.ModelType),
                p.BaseUrl,
                ApiKey = MaskApiKey(p.ApiKey),
                p.ModelName,
                p.MaxOutputTokens,
                p.IsEnabled,
                p.IsDefault,
                p.Capabilities
            });
            return Results.Ok(result);
        })
        .WithTags("Providers");

        endpoints.MapPost("/providers", (ProviderCreateRequest req, ProviderConfigStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.DisplayName))
                return ApiErrors.BadRequest("DisplayName is required.");
            if (string.IsNullOrWhiteSpace(req.ModelName))
                return ApiErrors.BadRequest("ModelName is required.");
            if (string.IsNullOrWhiteSpace(req.ApiKey))
                return ApiErrors.BadRequest("ApiKey is required.");

            ProviderConfig config = new()
            {
                DisplayName = req.DisplayName.Trim(),
                Protocol = ParseProtocol(req.Protocol),
                ModelType = ParseModelType(req.ModelType),
                BaseUrl = string.IsNullOrWhiteSpace(req.BaseUrl) ? null : req.BaseUrl.Trim(),
                ApiKey = req.ApiKey.Trim(),
                ModelName = req.ModelName.Trim(),
                MaxOutputTokens = req.MaxOutputTokens,
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
                return ApiErrors.BadRequest("Id is required.");

            ProviderConfig incoming = new()
            {
                DisplayName = req.DisplayName?.Trim() ?? string.Empty,
                Protocol = ParseProtocol(req.Protocol),
                ModelType = ParseModelType(req.ModelType),
                BaseUrl = string.IsNullOrWhiteSpace(req.BaseUrl) ? null : req.BaseUrl.Trim(),
                ApiKey = req.ApiKey?.Trim() ?? string.Empty,
                ModelName = req.ModelName?.Trim() ?? string.Empty,
                MaxOutputTokens = req.MaxOutputTokens ?? 8192,
                IsEnabled = req.IsEnabled,
                Capabilities = req.Capabilities ?? new()
            };

            ProviderConfig? updated = store.Update(req.Id, incoming);
            if (updated is null)
                return ApiErrors.NotFound($"Provider '{req.Id}' not found.");

            return Results.Ok(new { updated.Id });
        })
        .WithTags("Providers");

        endpoints.MapPost("/providers/delete", (ProviderDeleteRequest req, ProviderConfigStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return ApiErrors.BadRequest("Id is required.");

            bool deleted = store.Delete(req.Id);
            if (!deleted)
                return ApiErrors.NotFound($"Provider '{req.Id}' not found.");

            return Results.Ok();
        })
        .WithTags("Providers");

        endpoints.MapPost("/providers/set-default", (ProviderSetDefaultRequest req, ProviderConfigStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return ApiErrors.BadRequest("Id is required.");

            bool ok = store.SetDefault(req.Id);
            if (!ok)
                return ApiErrors.NotFound($"Provider '{req.Id}' not found.");

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
