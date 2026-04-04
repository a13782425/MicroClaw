using MicroClaw.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MicroClaw.Pet.Prompt;

/// <summary>
/// Pet 提示词 YAML 文件读写。
/// <para>
/// 管理 <c>{sessionsDir}/{sessionId}/pet/</c> 下的三个 YAML 文件：
/// <list type="bullet">
///   <item><c>personality.yaml</c> — 人格提示词</item>
///   <item><c>dispatch-rules.yaml</c> — 调度规则</item>
///   <item><c>knowledge-interests.yaml</c> — 学习方向</item>
/// </list>
/// 首次读取时若文件不存在，使用 PetFactory 写入的默认模板返回默认值。
/// </para>
/// </summary>
public sealed class PetPromptStore
{
    private readonly string _sessionsDir;

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve)
        .Build();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public PetPromptStore(MicroClawConfigEnv env)
    {
        ArgumentNullException.ThrowIfNull(env);
        _sessionsDir = env.SessionsDir;
    }

    /// <summary>仅供测试使用：直接指定 sessions 根目录。</summary>
    internal PetPromptStore(string sessionsDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionsDir);
        _sessionsDir = sessionsDir;
    }

    // ── personality.yaml ──────────────────────────────────────────────────

    /// <summary>读取 Pet 人格提示词。文件不存在时返回默认值。</summary>
    public async Task<PersonalityPrompt> LoadPersonalityAsync(string sessionId, CancellationToken ct = default)
    {
        var path = GetFilePath(sessionId, "personality.yaml");
        return await LoadYamlAsync<PersonalityPrompt>(path, ct).ConfigureAwait(false)
               ?? PersonalityPrompt.Default;
    }

    /// <summary>保存 Pet 人格提示词（自动创建 .bak 备份）。</summary>
    public async Task SavePersonalityAsync(string sessionId, PersonalityPrompt prompt, CancellationToken ct = default)
    {
        var path = GetFilePath(sessionId, "personality.yaml");
        await SaveYamlAsync(path, prompt, ct).ConfigureAwait(false);
    }

    // ── dispatch-rules.yaml ──────────────────────────────────────────────

    /// <summary>读取 Pet 调度规则。文件不存在时返回默认值。</summary>
    public async Task<DispatchRules> LoadDispatchRulesAsync(string sessionId, CancellationToken ct = default)
    {
        var path = GetFilePath(sessionId, "dispatch-rules.yaml");
        return await LoadYamlAsync<DispatchRules>(path, ct).ConfigureAwait(false)
               ?? DispatchRules.Default;
    }

    /// <summary>保存 Pet 调度规则（自动创建 .bak 备份）。</summary>
    public async Task SaveDispatchRulesAsync(string sessionId, DispatchRules rules, CancellationToken ct = default)
    {
        var path = GetFilePath(sessionId, "dispatch-rules.yaml");
        await SaveYamlAsync(path, rules, ct).ConfigureAwait(false);
    }

    // ── knowledge-interests.yaml ─────────────────────────────────────────

    /// <summary>读取 Pet 学习方向。文件不存在时返回默认值。</summary>
    public async Task<KnowledgeInterests> LoadKnowledgeInterestsAsync(string sessionId, CancellationToken ct = default)
    {
        var path = GetFilePath(sessionId, "knowledge-interests.yaml");
        return await LoadYamlAsync<KnowledgeInterests>(path, ct).ConfigureAwait(false)
               ?? KnowledgeInterests.Default;
    }

    /// <summary>保存 Pet 学习方向（自动创建 .bak 备份）。</summary>
    public async Task SaveKnowledgeInterestsAsync(string sessionId, KnowledgeInterests interests, CancellationToken ct = default)
    {
        var path = GetFilePath(sessionId, "knowledge-interests.yaml");
        await SaveYamlAsync(path, interests, ct).ConfigureAwait(false);
    }

    // ── 通用辅助 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 读取所有三个提示词文件，聚合为一个完整的文本摘要（供 LLM 决策输入）。
    /// </summary>
    public async Task<string> LoadAllAsTextAsync(string sessionId, CancellationToken ct = default)
    {
        var personality = await LoadPersonalityAsync(sessionId, ct).ConfigureAwait(false);
        var rules = await LoadDispatchRulesAsync(sessionId, ct).ConfigureAwait(false);
        var interests = await LoadKnowledgeInterestsAsync(sessionId, ct).ConfigureAwait(false);

        return $"""
            ## 人格设定
            {personality.Persona}
            语气: {personality.Tone}, 语言: {personality.Language}

            ## 调度规则
            默认策略: {rules.DefaultStrategy}
            {string.Join("\n", rules.Rules.Select(r => $"- 匹配: {r.Pattern} → {r.PreferredModelType} ({r.Notes})"))}

            ## 学习方向
            {string.Join("\n", interests.Topics.Select(t => $"- {t.Name} [{t.Priority}]: {t.Description}"))}
            """;
    }

    private string GetFilePath(string sessionId, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return Path.Combine(_sessionsDir, sessionId, "pet", fileName);
    }

    private static async Task<T?> LoadYamlAsync<T>(string path, CancellationToken ct) where T : class
    {
        if (!File.Exists(path)) return null;

        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) return null;

        // 去除 YAML 注释行后反序列化
        return YamlDeserializer.Deserialize<T>(content);
    }

    private static async Task SaveYamlAsync<T>(string path, T data, CancellationToken ct) where T : class
    {
        ArgumentNullException.ThrowIfNull(data);

        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);

        // 创建 .bak 备份
        if (File.Exists(path))
        {
            var bakPath = path + ".bak";
            File.Copy(path, bakPath, overwrite: true);
        }

        var yaml = YamlSerializer.Serialize(data);
        await File.WriteAllTextAsync(path, yaml, ct).ConfigureAwait(false);
    }
}

// ── YAML 模型 ────────────────────────────────────────────────────────────

/// <summary>Pet 人格提示词模型（personality.yaml）。</summary>
public sealed class PersonalityPrompt
{
    public string Persona { get; set; } = string.Empty;
    public string Tone { get; set; } = "professional";
    public string Language { get; set; } = "zh-cn";

    public static readonly PersonalityPrompt Default = new()
    {
        Persona = "你是一个智能会话助理（Pet），负责理解用户需求，选择合适的 Agent 和工具执行任务。\n你善于学习用户习惯，在对话中积累知识，并随着时间不断改进自己的工作方式。\n",
        Tone = "professional",
        Language = "zh-cn",
    };
}

/// <summary>Pet 调度规则模型（dispatch-rules.yaml）。</summary>
public sealed class DispatchRules
{
    public string DefaultStrategy { get; set; } = "default";
    public List<DispatchRule> Rules { get; set; } = [];

    public static readonly DispatchRules Default = new()
    {
        DefaultStrategy = "default",
        Rules =
        [
            new() { Pattern = ".*代码.*|.*编程.*|.*bug.*", PreferredModelType = "quality", Notes = "代码相关问题优先使用高质量模型" },
            new() { Pattern = ".*翻译.*|.*简单.*", PreferredModelType = "cost", Notes = "简单任务使用低成本模型" },
        ],
    };
}

/// <summary>调度规则条目。</summary>
public sealed class DispatchRule
{
    public string Pattern { get; set; } = string.Empty;
    public string PreferredModelType { get; set; } = "default";
    public string Notes { get; set; } = string.Empty;
}

/// <summary>Pet 学习方向模型（knowledge-interests.yaml）。</summary>
public sealed class KnowledgeInterests
{
    public List<KnowledgeTopic> Topics { get; set; } = [];

    public static readonly KnowledgeInterests Default = new()
    {
        Topics =
        [
            new() { Name = "user_preferences", Description = "用户偏好与工作习惯", Priority = "high" },
            new() { Name = "domain_knowledge", Description = "会话涉及的领域知识", Priority = "medium" },
            new() { Name = "error_patterns", Description = "失败模式和错误处理经验", Priority = "medium" },
        ],
    };
}

/// <summary>学习方向条目。</summary>
public sealed class KnowledgeTopic
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium";
}
