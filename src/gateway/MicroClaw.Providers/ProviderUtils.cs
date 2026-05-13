using System.Text.Json;
using System.Text.RegularExpressions;
namespace MicroClaw.Providers;
public static class ProviderUtils
{
    public static ProviderProtocol ParseProtocol(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "openai" => ProviderProtocol.OpenAI,
            "openai-responses" => ProviderProtocol.OpenAI,
            "anthropic" => ProviderProtocol.Anthropic,
            _ => ProviderProtocol.OpenAI
        };
    
    public static string SerializeProtocol(ProviderProtocol protocol) =>
        protocol switch
        {
            ProviderProtocol.OpenAI => "openai",
            ProviderProtocol.Anthropic => "anthropic",
            _ => "openai"
        };
    
    public static ModelType ParseModelType(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "embedding" => ModelType.Embedding,
            _ => ModelType.Chat
        };
    
    public static string SerializeModelType(ModelType modelType) =>
        modelType switch
        {
            ModelType.Embedding => "embedding",
            _ => "chat"
        };
    
    public static ProviderCapabilities DeserializeCapabilities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ProviderCapabilities();
        try
        {
            return JsonSerializer.Deserialize<ProviderCapabilities>(json) ?? new ProviderCapabilities();
        }
        catch
        {
            return new ProviderCapabilities();
        }
    }
    
    internal static string? ResolveEnvVars(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Regex.Replace(value, @"\$\{([^}]+)\}", m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }
}