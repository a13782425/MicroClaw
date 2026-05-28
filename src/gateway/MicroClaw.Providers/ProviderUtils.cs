using System;
using System.Collections.Generic;
using System.Text;

namespace MicroClaw.Providers;

/// <summary>
/// Provider 通用工具类：整合 YAML 配置解析、模型能力/模态描述、内置场景目录，
/// 为 <see cref="ModelProviderObject"/>、<see cref="ModelProviderService"/> 及 UI 层提供统一入口。
/// </summary>
public static class ProviderUtils
{
    // ═══════════════════════════════════════════════════════════════
    //  配置字符串 → 强类型枚举解析
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 将 YAML 中的 <c>api_kind</c> 字符串解析为 <see cref="ModelProviderApiKind"/>。
    /// 未识别值默认回退为 <see cref="ModelProviderApiKind.OpenAI"/>。
    /// </summary>
    public static ModelProviderApiKind ParseApiKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "anthropic" or "claude" => ModelProviderApiKind.Anthropic,
            "openai" or "openai-responses" => ModelProviderApiKind.OpenAI,
            "other" or "openai-compatible" => ModelProviderApiKind.Other,
            _ => ModelProviderApiKind.OpenAI,
        };

    /// <summary>
    /// 将 YAML 中的 <c>model_kind</c> 字符串解析为 <see cref="ModelKind"/>。
    /// 未识别值默认回退为 <see cref="ModelKind.Chat"/>。
    /// </summary>
    public static ModelKind ParseModelKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "embedding" or "embeddings" => ModelKind.Embedding,
            _ => ModelKind.Chat,
        };

    /// <summary>
    /// 将 YAML 中的 <c>capabilities</c> 字符串列表解析为 <see cref="ModelCapability"/> 位标志。
    /// 空或 null 返回 <see cref="ModelCapability.None"/>。
    /// </summary>
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

    /// <summary>
    /// 将 YAML 中的 <c>input_modalities</c> / <c>output_modalities</c> 字符串列表解析为 <see cref="ModelModality"/> 位标志。
    /// 未匹配到任何已知模态时返回 <paramref name="defaultModality"/>。
    /// </summary>
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

    /// <summary>
    /// 标准化 BaseUrl：trim 空白并移除末尾斜杠。空值返回 null。
    /// </summary>
    public static string? NormalizeBaseUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');

    // ═══════════════════════════════════════════════════════════════
    //  模态中文显示名（面向 UI）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 模态标识 → 中文显示名映射（key 与 YAML 中 <c>input_modalities</c> / <c>output_modalities</c> 的值一致）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ModalityDescriptions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = "文本",
            ["image"] = "图像",
            ["audio"] = "音频",
            ["video"] = "视频",
            ["file"] = "文件",
        };

    /// <summary>
    /// 将 <see cref="ModelModality"/> 位标志展开为中文显示名列表（用于 UI 展示）。
    /// </summary>
    public static IReadOnlyList<string> GetModalityLabels(ModelModality modality)
    {
        var labels = new List<string>(5);
        foreach (var kvp in ModalityDescriptions)
        {
            ModelModality flag = kvp.Key.ToLowerInvariant() switch
            {
                "text" => ModelModality.Text,
                "image" => ModelModality.Image,
                "audio" => ModelModality.Audio,
                "video" => ModelModality.Video,
                "file" => ModelModality.File,
                _ => ModelModality.None,
            };
            if ((modality & flag) != 0)
                labels.Add(kvp.Value);
        }
        return labels;
    }

    // ═══════════════════════════════════════════════════════════════
    //  能力/功能中文显示名（面向 UI）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 能力标识 → 中文显示名映射（key 与 YAML 中 <c>capabilities</c> 的值一致）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> FeatureDescriptions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tool_calling"] = "工具调用",
            ["responses_api"] = "Responses API",
        };

    // ═══════════════════════════════════════════════════════════════
    //  内置场景目录
    // ═══════════════════════════════════════════════════════════════

    /// <summary>场景：工具调用。</summary>
    public const string ScenarioToolCalling = "tool_calling";
    /// <summary>场景：任务规划。</summary>
    public const string ScenarioPlanning = "planning";
    /// <summary>场景：代码生成。</summary>
    public const string ScenarioCoding = "coding";
    /// <summary>场景：图像理解。</summary>
    public const string ScenarioVision = "vision";
    /// <summary>场景：文档摘要。</summary>
    public const string ScenarioSummarization = "summarization";
    /// <summary>场景：视频理解。</summary>
    public const string ScenarioVideoUnderstanding = "video_understanding";

    /// <summary>内置场景键集合，顺序代表 UI 展示顺序。</summary>
    public static IReadOnlyList<string> BuiltInScenarioKeys { get; } =
    [
        ScenarioToolCalling,
        ScenarioPlanning,
        ScenarioCoding,
        ScenarioVision,
        ScenarioSummarization,
        ScenarioVideoUnderstanding,
    ];

    /// <summary>场景标识 → 中文显示名映射。</summary>
    public static IReadOnlyDictionary<string, string> ScenarioDescriptions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ScenarioToolCalling] = "工具调用",
            [ScenarioPlanning] = "任务规划",
            [ScenarioCoding] = "代码生成",
            [ScenarioVision] = "图像理解",
            [ScenarioSummarization] = "文档摘要",
            [ScenarioVideoUnderstanding] = "视频理解",
        };

    /// <summary>
    /// 用默认值补齐缺失的内置场景键，并保留输入中的额外键。
    /// 所有评分自动 clamp 到 [0, 100]。
    /// </summary>
    public static IReadOnlyDictionary<string, ModelScenarioScore> NormalizeScenarioScores(
        IReadOnlyDictionary<string, ModelScenarioScore>? source)
    {
        var result = new Dictionary<string, ModelScenarioScore>(StringComparer.OrdinalIgnoreCase);

        if (source is not null)
        {
            foreach (var kvp in source)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                result[kvp.Key.Trim()] = kvp.Value?.Clamp() ?? ModelScenarioScore.Default;
            }
        }

        foreach (string key in BuiltInScenarioKeys)
        {
            if (!result.ContainsKey(key))
                result[key] = ModelScenarioScore.Default;
        }

        return result;
    }
}
