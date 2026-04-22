using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MicroClaw.Services;

/// <summary>
/// 系统 YAML 配置读取与回写服务。
/// </summary>
public sealed class ConfigService
{
    private readonly string _configDir;

    public ConfigService()
    {
        _configDir = Path.Combine(MicroClawConfig.Env.Home, "config");
    }

    // ── Read ──────────────────────────────────────────────────────────────

    public SystemConfigDto GetSystemConfig()
    {
        var agent = MicroClawConfig.Get<AgentsOptions>();
        var skills = MicroClawConfig.Get<SkillOptions>();
        var emotion = MicroClawConfig.Get<EmotionOptions>();

        return new SystemConfigDto
        {
            Agent = new AgentConfigSection { SubAgentMaxDepth = agent.SubAgentMaxDepth },
            Skills = new SkillsConfigSection { AdditionalFolders = skills.AdditionalFolders },
            Emotion = EmotionConfigSection.FromOptions(emotion)
        };
    }

    // ── Write ─────────────────────────────────────────────────────────────

    public void UpdateAgentConfig(AgentConfigSection section)
    {
        var current = MicroClawConfig.Get<AgentsOptions>();
        var updated = new AgentsOptions
        {
            SubAgentMaxDepth = section.SubAgentMaxDepth,
            Items = current.Items
        };
        MicroClawConfig.Save(updated);
    }

    public void UpdateSkillsConfig(SkillsConfigSection section)
    {
        SkillOptions current = MicroClawConfig.Get<SkillOptions>();

        var filePath = Path.Combine(_configDir, "skills.yaml");
        if (!File.Exists(filePath))
            MicroClawConfig.Save(current);

        var yaml = LoadYaml(filePath);
        var root = EnsureMappingRoot(yaml);
        var skillsNode = EnsureChildMapping(root, "skills");

        var seqNode = new YamlSequenceNode();
        foreach (var folder in section.AdditionalFolders)
            seqNode.Add(new YamlScalarNode(folder));

        skillsNode.Children[new YamlScalarNode("additional_folders")] = seqNode;

        BackupAndSave(filePath, yaml);

        MicroClawConfig.Update(new SkillOptions
        {
            AllowCommandInjection = current.AllowCommandInjection,
            CatalogCharBudget = current.CatalogCharBudget,
            DefaultFolder = current.DefaultFolder,
            AdditionalFolders = [.. section.AdditionalFolders]
        });
    }

    public void UpdateEmotionConfig(EmotionConfigSection section)
    {
        EmotionOptions current = MicroClawConfig.Get<EmotionOptions>();

        var filePath = Path.Combine(_configDir, "emotion.yaml");
        if (!File.Exists(filePath))
            MicroClawConfig.Save(current);

        var yaml = LoadYaml(filePath);
        var root = EnsureMappingRoot(yaml);
        var emotionNode = EnsureChildMapping(root, "emotion");

        static YamlScalarNode S(string v) => new(v);
        static YamlScalarNode N(float v) => new(v.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
        static YamlScalarNode I(int v) => new(v.ToString());

        emotionNode.Children[S("cautious_alertness_threshold")] = I(section.CautiousAlertnessThreshold);
        emotionNode.Children[S("cautious_confidence_threshold")] = I(section.CautiousConfidenceThreshold);
        emotionNode.Children[S("explore_min_curiosity")] = I(section.ExploreMinCuriosity);
        emotionNode.Children[S("explore_min_mood")] = I(section.ExploreMinMood);
        emotionNode.Children[S("rest_max_alertness")] = I(section.RestMaxAlertness);
        emotionNode.Children[S("rest_max_mood")] = I(section.RestMaxMood);

        emotionNode.Children[S("normal_temperature")] = N(section.Normal.Temperature);
        emotionNode.Children[S("normal_top_p")] = N(section.Normal.TopP);
        emotionNode.Children[S("normal_system_prompt_suffix")] = S(section.Normal.SystemPromptSuffix);

        emotionNode.Children[S("explore_temperature")] = N(section.Explore.Temperature);
        emotionNode.Children[S("explore_top_p")] = N(section.Explore.TopP);
        emotionNode.Children[S("explore_system_prompt_suffix")] = S(section.Explore.SystemPromptSuffix);

        emotionNode.Children[S("cautious_temperature")] = N(section.Cautious.Temperature);
        emotionNode.Children[S("cautious_top_p")] = N(section.Cautious.TopP);
        emotionNode.Children[S("cautious_system_prompt_suffix")] = S(section.Cautious.SystemPromptSuffix);

        emotionNode.Children[S("rest_temperature")] = N(section.Rest.Temperature);
        emotionNode.Children[S("rest_top_p")] = N(section.Rest.TopP);
        emotionNode.Children[S("rest_system_prompt_suffix")] = S(section.Rest.SystemPromptSuffix);

        // 写入事件加减分 delta
        WriteDelta(emotionNode, "delta_message_success",    section.DeltaMessageSuccess);
        WriteDelta(emotionNode, "delta_message_failed",     section.DeltaMessageFailed);
        WriteDelta(emotionNode, "delta_tool_success",       section.DeltaToolSuccess);
        WriteDelta(emotionNode, "delta_tool_error",         section.DeltaToolError);
        WriteDelta(emotionNode, "delta_user_satisfied",     section.DeltaUserSatisfied);
        WriteDelta(emotionNode, "delta_user_dissatisfied",  section.DeltaUserDissatisfied);
        WriteDelta(emotionNode, "delta_task_completed",     section.DeltaTaskCompleted);
        WriteDelta(emotionNode, "delta_task_failed",        section.DeltaTaskFailed);
        WriteDelta(emotionNode, "delta_pain_high",          section.DeltaPainHigh);
        WriteDelta(emotionNode, "delta_pain_critical",      section.DeltaPainCritical);

        BackupAndSave(filePath, yaml);

        // 热更新内存中的配置
        MicroClawConfig.Update(section.ToOptions());
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static YamlStream LoadYaml(string filePath)
    {
        var yaml = new YamlStream();
        using var reader = new StreamReader(filePath);
        yaml.Load(reader);
        return yaml;
    }

    private static YamlMappingNode EnsureMappingRoot(YamlStream yaml)
    {
        if (yaml.Documents.Count == 0)
            yaml.Documents.Add(new YamlDocument(new YamlMappingNode()));

        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    private static YamlMappingNode EnsureChildMapping(YamlMappingNode root, string key)
    {
        var scalarKey = new YamlScalarNode(key);
        if (root.Children.TryGetValue(scalarKey, out var existing) && existing is YamlMappingNode existingMapping)
            return existingMapping;

        var newMapping = new YamlMappingNode();
        root.Children[scalarKey] = newMapping;
        return newMapping;
    }

    private static void BackupAndSave(string filePath, YamlStream yaml)
    {
        // 写入前备份
        var bakPath = filePath + ".bak";
        File.Copy(filePath, bakPath, overwrite: true);

        using var writer = new StreamWriter(filePath);
        yaml.Save(writer, assignAnchors: false);
    }

    private static void WriteDelta(YamlMappingNode parent, string key, EmotionDeltaConfigSection delta)
    {
        var node = new YamlMappingNode();
        if (delta.Alertness.HasValue)  node.Add("alertness",  delta.Alertness.Value.ToString());
        if (delta.Mood.HasValue)       node.Add("mood",       delta.Mood.Value.ToString());
        if (delta.Curiosity.HasValue)  node.Add("curiosity",  delta.Curiosity.Value.ToString());
        if (delta.Confidence.HasValue) node.Add("confidence", delta.Confidence.Value.ToString());
        parent.Children[new YamlScalarNode(key)] = node;
    }

}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed class SystemConfigDto
{
    public AgentConfigSection Agent { get; set; } = new();
    public SkillsConfigSection Skills { get; set; } = new();
    public EmotionConfigSection Emotion { get; set; } = new();
}

public sealed class AgentConfigSection
{
    public int SubAgentMaxDepth { get; set; } = 3;
}

public sealed class SkillsConfigSection
{
    public List<string> AdditionalFolders { get; set; } = [];
}

public sealed class BehaviorProfileConfigSection
{
    public float Temperature { get; set; }
    public float TopP { get; set; }
    public string SystemPromptSuffix { get; set; } = string.Empty;
}

public sealed class EmotionDeltaConfigSection
{
    public int? Alertness { get; set; }
    public int? Mood { get; set; }
    public int? Curiosity { get; set; }
    public int? Confidence { get; set; }

    public static EmotionDeltaConfigSection FromOptions(EmotionDeltaOptions o) => new()
    {
        Alertness  = o.Alertness,
        Mood       = o.Mood,
        Curiosity  = o.Curiosity,
        Confidence = o.Confidence,
    };

    public EmotionDeltaOptions ToOptions() => new()
    {
        Alertness  = Alertness,
        Mood       = Mood,
        Curiosity  = Curiosity,
        Confidence = Confidence,
    };
}

public sealed class EmotionConfigSection
{
    public int CautiousAlertnessThreshold { get; set; } = 70;
    public int CautiousConfidenceThreshold { get; set; } = 30;
    public int ExploreMinCuriosity { get; set; } = 70;
    public int ExploreMinMood { get; set; } = 60;
    public int RestMaxAlertness { get; set; } = 30;
    public int RestMaxMood { get; set; } = 40;
    public BehaviorProfileConfigSection Normal { get; set; } = new() { Temperature = 0.7f, TopP = 0.9f };
    public BehaviorProfileConfigSection Explore { get; set; } = new() { Temperature = 1.1f, TopP = 0.95f, SystemPromptSuffix = "请大胆探索，鼓励创造性思维，给出多样化的想法。" };
    public BehaviorProfileConfigSection Cautious { get; set; } = new() { Temperature = 0.3f, TopP = 0.8f, SystemPromptSuffix = "请谨慎行事，仔细验证每一步，不确定时优先寻求确认而非猜测。" };
    public BehaviorProfileConfigSection Rest { get; set; } = new() { Temperature = 0.5f, TopP = 0.85f, SystemPromptSuffix = "请简明扼要地作答，避免过度展开。" };

    // 事件加减分
    public EmotionDeltaConfigSection DeltaMessageSuccess   { get; set; } = new() { Mood = +3, Confidence = +2 };
    public EmotionDeltaConfigSection DeltaMessageFailed    { get; set; } = new() { Alertness = +8, Mood = -5, Confidence = -5 };
    public EmotionDeltaConfigSection DeltaToolSuccess      { get; set; } = new() { Curiosity = +2, Confidence = +3 };
    public EmotionDeltaConfigSection DeltaToolError        { get; set; } = new() { Alertness = +10, Mood = -3, Confidence = -5 };
    public EmotionDeltaConfigSection DeltaUserSatisfied    { get; set; } = new() { Mood = +10, Confidence = +5 };
    public EmotionDeltaConfigSection DeltaUserDissatisfied { get; set; } = new() { Mood = -10, Confidence = -5, Alertness = +5 };
    public EmotionDeltaConfigSection DeltaTaskCompleted    { get; set; } = new() { Mood = +8, Confidence = +8, Alertness = -5 };
    public EmotionDeltaConfigSection DeltaTaskFailed       { get; set; } = new() { Alertness = +10, Mood = -8, Confidence = -8 };
    public EmotionDeltaConfigSection DeltaPainHigh         { get; set; } = new() { Alertness = +22, Mood = -5, Confidence = -18 };
    public EmotionDeltaConfigSection DeltaPainCritical     { get; set; } = new() { Alertness = +32, Mood = -10, Confidence = -28 };

    public static EmotionConfigSection FromOptions(EmotionOptions o) => new()
    {
        CautiousAlertnessThreshold = o.CautiousAlertnessThreshold,
        CautiousConfidenceThreshold = o.CautiousConfidenceThreshold,
        ExploreMinCuriosity = o.ExploreMinCuriosity,
        ExploreMinMood = o.ExploreMinMood,
        RestMaxAlertness = o.RestMaxAlertness,
        RestMaxMood = o.RestMaxMood,
        Normal   = new() { Temperature = o.NormalTemperature,   TopP = o.NormalTopP,   SystemPromptSuffix = o.NormalSystemPromptSuffix },
        Explore  = new() { Temperature = o.ExploreTemperature,  TopP = o.ExploreTopP,  SystemPromptSuffix = o.ExploreSystemPromptSuffix },
        Cautious = new() { Temperature = o.CautiousTemperature, TopP = o.CautiousTopP, SystemPromptSuffix = o.CautiousSystemPromptSuffix },
        Rest     = new() { Temperature = o.RestTemperature,     TopP = o.RestTopP,     SystemPromptSuffix = o.RestSystemPromptSuffix },
        DeltaMessageSuccess   = EmotionDeltaConfigSection.FromOptions(o.DeltaMessageSuccess),
        DeltaMessageFailed    = EmotionDeltaConfigSection.FromOptions(o.DeltaMessageFailed),
        DeltaToolSuccess      = EmotionDeltaConfigSection.FromOptions(o.DeltaToolSuccess),
        DeltaToolError        = EmotionDeltaConfigSection.FromOptions(o.DeltaToolError),
        DeltaUserSatisfied    = EmotionDeltaConfigSection.FromOptions(o.DeltaUserSatisfied),
        DeltaUserDissatisfied = EmotionDeltaConfigSection.FromOptions(o.DeltaUserDissatisfied),
        DeltaTaskCompleted    = EmotionDeltaConfigSection.FromOptions(o.DeltaTaskCompleted),
        DeltaTaskFailed       = EmotionDeltaConfigSection.FromOptions(o.DeltaTaskFailed),
        DeltaPainHigh         = EmotionDeltaConfigSection.FromOptions(o.DeltaPainHigh),
        DeltaPainCritical     = EmotionDeltaConfigSection.FromOptions(o.DeltaPainCritical),
    };

    public EmotionOptions ToOptions() => new()
    {
        CautiousAlertnessThreshold = CautiousAlertnessThreshold,
        CautiousConfidenceThreshold = CautiousConfidenceThreshold,
        ExploreMinCuriosity = ExploreMinCuriosity,
        ExploreMinMood = ExploreMinMood,
        RestMaxAlertness = RestMaxAlertness,
        RestMaxMood = RestMaxMood,
        NormalTemperature   = Normal.Temperature,   NormalTopP   = Normal.TopP,   NormalSystemPromptSuffix   = Normal.SystemPromptSuffix,
        ExploreTemperature  = Explore.Temperature,  ExploreTopP  = Explore.TopP,  ExploreSystemPromptSuffix  = Explore.SystemPromptSuffix,
        CautiousTemperature = Cautious.Temperature, CautiousTopP = Cautious.TopP, CautiousSystemPromptSuffix = Cautious.SystemPromptSuffix,
        RestTemperature     = Rest.Temperature,     RestTopP     = Rest.TopP,     RestSystemPromptSuffix     = Rest.SystemPromptSuffix,
        DeltaMessageSuccess   = DeltaMessageSuccess.ToOptions(),
        DeltaMessageFailed    = DeltaMessageFailed.ToOptions(),
        DeltaToolSuccess      = DeltaToolSuccess.ToOptions(),
        DeltaToolError        = DeltaToolError.ToOptions(),
        DeltaUserSatisfied    = DeltaUserSatisfied.ToOptions(),
        DeltaUserDissatisfied = DeltaUserDissatisfied.ToOptions(),
        DeltaTaskCompleted    = DeltaTaskCompleted.ToOptions(),
        DeltaTaskFailed       = DeltaTaskFailed.ToOptions(),
        DeltaPainHigh         = DeltaPainHigh.ToOptions(),
        DeltaPainCritical     = DeltaPainCritical.ToOptions(),
    };
}
