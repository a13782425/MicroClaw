using System.Text.RegularExpressions;
using MicroClaw.Configuration;

namespace MicroClaw.Providers.Mapping;

/// <summary>
/// 针对 <see cref="ProviderEntityConfig"/> 的纯函数解析工具。
/// 不再产出独立运行时档案；由 <see cref="ModelProviderObject"/> 在访问 protected 属性时按需调用。
/// </summary>
internal static class ProviderConfigOps
{
    private static readonly Regex EnvVarPattern = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    public static ModelProviderApiKind ParseApiKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "anthropic" or "claude" => ModelProviderApiKind.Anthropic,
            "openai" or "openai-responses" => ModelProviderApiKind.OpenAI,
            "other" or "openai-compatible" => ModelProviderApiKind.Other,
            _ => ModelProviderApiKind.OpenAI,
        };

    public static ModelKind ParseModelKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "embedding" or "embeddings" => ModelKind.Embedding,
            _ => ModelKind.Chat,
        };

    public static ModelCapability ParseCapabilities(IEnumerable<string>? values)
    {
        if (values is null) return ModelCapability.None;
        var acc = ModelCapability.None;
        foreach (string v in values)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            acc |= v.Trim().ToLowerInvariant() switch
            {
                "tool_calling" or "function_calling" or "tools" => ModelCapability.ToolCalling,
                "responses_api" or "responses" => ModelCapability.ResponsesApi,
                _ => ModelCapability.None,
            };
        }
        return acc;
    }

    public static ModelModality ParseModalities(IEnumerable<string>? values, ModelModality defaultModality)
    {
        if (values is null) return defaultModality;
        var acc = ModelModality.None;
        bool any = false;
        foreach (string v in values)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            any = true;
            acc |= v.Trim().ToLowerInvariant() switch
            {
                "text" => ModelModality.Text,
                "image" => ModelModality.Image,
                "audio" => ModelModality.Audio,
                "video" => ModelModality.Video,
                "file" => ModelModality.File,
                _ => ModelModality.None,
            };
        }
        return any && acc != ModelModality.None ? acc : defaultModality;
    }

    public static ModelPricing ToPricing(ProviderPricingConfig? cfg)
    {
        if (cfg is null) return ModelPricing.Empty;
        return new ModelPricing
        {
            InputPerMillionTokens = NonNegative(cfg.InputPerMillionTokens),
            OutputPerMillionTokens = NonNegative(cfg.OutputPerMillionTokens),
            CachedInputPerMillionTokens = NonNegative(cfg.CachedInputPerMillionTokens),
            CachedOutputPerMillionTokens = NonNegative(cfg.CachedOutputPerMillionTokens),
        };

        static decimal? NonNegative(decimal? v) => v is null ? null : Math.Max(0m, v.Value);
    }

    public static string? ResolveEnv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return EnvVarPattern.Replace(
            value,
            m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }

    public static string? NormalizeBaseUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
}
