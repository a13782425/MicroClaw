namespace MicroClaw.Providers;

/// <summary>
/// 内置场景目录。为每一份 <see cref="ModelProfile"/> 提供一组默认场景键，
/// 缺失时自动补全为 <see cref="ModelScenarioScore.Default"/>；YAML 中出现的未知键会被保留。
/// </summary>
public static class ModelScenarioCatalog
{
    public const string ToolCalling = "tool_calling";
    public const string Planning = "planning";
    public const string Coding = "coding";
    public const string Vision = "vision";
    public const string Summarization = "summarization";
    public const string VideoUnderstanding = "video_understanding";

    /// <summary>内置场景键集合，顺序代表展示顺序。</summary>
    public static IReadOnlyList<string> BuiltInKeys { get; } =
    [
        ToolCalling,
        Planning,
        Coding,
        Vision,
        Summarization,
        VideoUnderstanding,
    ];

    /// <summary>面向 UI 的场景描述（中文）。</summary>
    public static IReadOnlyDictionary<string, string> Descriptions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ToolCalling] = "工具调用",
            [Planning] = "任务规划",
            [Coding] = "代码生成",
            [Vision] = "图像理解",
            [Summarization] = "文档摘要",
            [VideoUnderstanding] = "视频理解",
        };

    /// <summary>用默认值补齐缺失的内置键，并保留输入中的额外键。</summary>
    public static IReadOnlyDictionary<string, ModelScenarioScore> Normalize(
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

        foreach (string key in BuiltInKeys)
        {
            if (!result.ContainsKey(key))
                result[key] = ModelScenarioScore.Default;
        }

        return result;
    }
}
