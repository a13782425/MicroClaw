using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Services;

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

    public static EmotionDeltaConfigSection FromOptions(EmotionDeltaOptions o) =>
        new()
        {
            Alertness = o.Alertness,
            Mood = o.Mood,
            Curiosity = o.Curiosity,
            Confidence = o.Confidence,
        };

    public EmotionDeltaOptions ToOptions() =>
        new()
        {
            Alertness = Alertness ?? 0,
            Mood = Mood ?? 0,
            Curiosity = Curiosity ?? 0,
            Confidence = Confidence ?? 0,
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

    public EmotionDeltaConfigSection DeltaMessageSuccess { get; set; } = new() { Mood = +3, Confidence = +2 };
    public EmotionDeltaConfigSection DeltaMessageFailed { get; set; } = new() { Alertness = +8, Mood = -5, Confidence = -5 };
    public EmotionDeltaConfigSection DeltaToolSuccess { get; set; } = new() { Curiosity = +2, Confidence = +3 };
    public EmotionDeltaConfigSection DeltaToolError { get; set; } = new() { Alertness = +10, Mood = -3, Confidence = -5 };
    public EmotionDeltaConfigSection DeltaUserSatisfied { get; set; } = new() { Mood = +10, Confidence = +5 };
    public EmotionDeltaConfigSection DeltaUserDissatisfied { get; set; } = new() { Mood = -10, Confidence = -5, Alertness = +5 };
    public EmotionDeltaConfigSection DeltaTaskCompleted { get; set; } = new() { Mood = +8, Confidence = +8, Alertness = -5 };
    public EmotionDeltaConfigSection DeltaTaskFailed { get; set; } = new() { Alertness = +10, Mood = -8, Confidence = -8 };
    public EmotionDeltaConfigSection DeltaPainHigh { get; set; } = new() { Alertness = +22, Mood = -5, Confidence = -18 };
    public EmotionDeltaConfigSection DeltaPainCritical { get; set; } = new() { Alertness = +32, Mood = -10, Confidence = -28 };

    public static EmotionConfigSection FromOptions(EmotionOptions o) =>
        new()
        {
            CautiousAlertnessThreshold = o.CautiousAlertnessThreshold,
            CautiousConfidenceThreshold = o.CautiousConfidenceThreshold,
            ExploreMinCuriosity = o.ExploreMinCuriosity,
            ExploreMinMood = o.ExploreMinMood,
            RestMaxAlertness = o.RestMaxAlertness,
            RestMaxMood = o.RestMaxMood,
            Normal = new() { Temperature = o.NormalTemperature, TopP = o.NormalTopP, SystemPromptSuffix = o.NormalSystemPromptSuffix },
            Explore = new() { Temperature = o.ExploreTemperature, TopP = o.ExploreTopP, SystemPromptSuffix = o.ExploreSystemPromptSuffix },
            Cautious = new() { Temperature = o.CautiousTemperature, TopP = o.CautiousTopP, SystemPromptSuffix = o.CautiousSystemPromptSuffix },
            Rest = new() { Temperature = o.RestTemperature, TopP = o.RestTopP, SystemPromptSuffix = o.RestSystemPromptSuffix },
            DeltaMessageSuccess = EmotionDeltaConfigSection.FromOptions(o.DeltaMessageSuccess),
            DeltaMessageFailed = EmotionDeltaConfigSection.FromOptions(o.DeltaMessageFailed),
            DeltaToolSuccess = EmotionDeltaConfigSection.FromOptions(o.DeltaToolSuccess),
            DeltaToolError = EmotionDeltaConfigSection.FromOptions(o.DeltaToolError),
            DeltaUserSatisfied = EmotionDeltaConfigSection.FromOptions(o.DeltaUserSatisfied),
            DeltaUserDissatisfied = EmotionDeltaConfigSection.FromOptions(o.DeltaUserDissatisfied),
            DeltaTaskCompleted = EmotionDeltaConfigSection.FromOptions(o.DeltaTaskCompleted),
            DeltaTaskFailed = EmotionDeltaConfigSection.FromOptions(o.DeltaTaskFailed),
            DeltaPainHigh = EmotionDeltaConfigSection.FromOptions(o.DeltaPainHigh),
            DeltaPainCritical = EmotionDeltaConfigSection.FromOptions(o.DeltaPainCritical),
        };

    public EmotionOptions ToOptions() =>
        new()
        {
            CautiousAlertnessThreshold = CautiousAlertnessThreshold,
            CautiousConfidenceThreshold = CautiousConfidenceThreshold,
            ExploreMinCuriosity = ExploreMinCuriosity,
            ExploreMinMood = ExploreMinMood,
            RestMaxAlertness = RestMaxAlertness,
            RestMaxMood = RestMaxMood,
            NormalTemperature = Normal.Temperature,
            NormalTopP = Normal.TopP,
            NormalSystemPromptSuffix = Normal.SystemPromptSuffix,
            ExploreTemperature = Explore.Temperature,
            ExploreTopP = Explore.TopP,
            ExploreSystemPromptSuffix = Explore.SystemPromptSuffix,
            CautiousTemperature = Cautious.Temperature,
            CautiousTopP = Cautious.TopP,
            CautiousSystemPromptSuffix = Cautious.SystemPromptSuffix,
            RestTemperature = Rest.Temperature,
            RestTopP = Rest.TopP,
            RestSystemPromptSuffix = Rest.SystemPromptSuffix,
            DeltaMessageSuccess = DeltaMessageSuccess.ToOptions(),
            DeltaMessageFailed = DeltaMessageFailed.ToOptions(),
            DeltaToolSuccess = DeltaToolSuccess.ToOptions(),
            DeltaToolError = DeltaToolError.ToOptions(),
            DeltaUserSatisfied = DeltaUserSatisfied.ToOptions(),
            DeltaUserDissatisfied = DeltaUserDissatisfied.ToOptions(),
            DeltaTaskCompleted = DeltaTaskCompleted.ToOptions(),
            DeltaTaskFailed = DeltaTaskFailed.ToOptions(),
            DeltaPainHigh = DeltaPainHigh.ToOptions(),
            DeltaPainCritical = DeltaPainCritical.ToOptions(),
        };
}
