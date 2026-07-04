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
    /// 未识别值默认回退为 <see cref="ModelProviderApiKind.OpenChat"/>。
    /// </summary>
    public static ModelProviderApiKind ParseApiKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "anthropic" or "claude" => ModelProviderApiKind.Anthropic,
            "openai-responses" or "responses" => ModelProviderApiKind.OpenResponses,
            _ => ModelProviderApiKind.OpenChat,
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
    /// 将 YAML 中的 <c>input_modalities</c> 字符串列表解析为 <see cref="ModelInputModality"/> 位标志。
    /// 支持 <c>tool_call</c>（工具调用能力）。未匹配到任何已知模态时返回 <paramref name="defaultModality"/>。
    /// </summary>
    public static ModelInputModality ParseInputModalities(IEnumerable<string>? values, ModelInputModality defaultModality)
    {
        if (values is null) return defaultModality;
        ModelInputModality acc = ModelInputModality.None;
        bool any = false;
        foreach (string v in values)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            any = true;
            acc |= v.Trim().ToLowerInvariant() switch
            {
                "text" => ModelInputModality.Text,
                "image" => ModelInputModality.Image,
                "audio" => ModelInputModality.Audio,
                "video" => ModelInputModality.Video,
                "file" => ModelInputModality.File,
                "tool_call" or "tool_calling" => ModelInputModality.ToolCall,
                _ => ModelInputModality.None,
            };
        }
        return any && acc != ModelInputModality.None ? acc : defaultModality;
    }

    /// <summary>
    /// 将 YAML 中的 <c>output_modalities</c> 字符串列表解析为 <see cref="ModelOutputModality"/> 位标志。
    /// 未匹配到任何已知模态时返回 <paramref name="defaultModality"/>。
    /// </summary>
    public static ModelOutputModality ParseOutputModalities(IEnumerable<string>? values, ModelOutputModality defaultModality)
    {
        if (values is null) return defaultModality;
        ModelOutputModality acc = ModelOutputModality.None;
        bool any = false;
        foreach (string v in values)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            any = true;
            acc |= v.Trim().ToLowerInvariant() switch
            {
                "text" => ModelOutputModality.Text,
                "image" => ModelOutputModality.Image,
                "audio" => ModelOutputModality.Audio,
                "video" => ModelOutputModality.Video,
                "file" => ModelOutputModality.File,
                _ => ModelOutputModality.None,
            };
        }
        return any && acc != ModelOutputModality.None ? acc : defaultModality;
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
    /// 输出模态标识 → 中文显示名映射（key 与 YAML 中 <c>output_modalities</c> 的值一致）。
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
    /// 输入模态标识 → 中文显示名映射（key 与 YAML 中 <c>input_modalities</c> 的值一致）。
    /// 相比输出模态多出 <c>tool_call</c>（工具调用能力）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> InputModalityDescriptions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = "文本",
            ["image"] = "图像",
            ["audio"] = "音频",
            ["video"] = "视频",
            ["file"] = "文件",
            ["tool_call"] = "工具调用",
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
