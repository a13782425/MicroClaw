using MicroClaw.Configuration;
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
        var agent = MicroClawConfig.Get<AgentOptions>();
        var skills = MicroClawConfig.Get<SkillOptions>();

        return new SystemConfigDto
        {
            Agent = new AgentConfigSection { SubAgentMaxDepth = agent.SubAgentMaxDepth },
            Skills = new SkillsConfigSection { AdditionalFolders = skills.AdditionalFolders }
        };
    }

    // ── Write ─────────────────────────────────────────────────────────────

    public void UpdateAgentConfig(AgentConfigSection section)
    {
        var filePath = Path.Combine(_configDir, "agent.yaml");
        EnsureFileExists(filePath, $"agent:\n  sub_agent_max_depth: 3\n");

        var yaml = LoadYaml(filePath);
        var root = EnsureMappingRoot(yaml);
        var agentNode = EnsureChildMapping(root, "agent");
        agentNode.Children[new YamlScalarNode("sub_agent_max_depth")] =
            new YamlScalarNode(section.SubAgentMaxDepth.ToString());

        BackupAndSave(filePath, yaml);
    }

    public void UpdateSkillsConfig(SkillsConfigSection section)
    {
        var filePath = Path.Combine(_configDir, "skills.yaml");
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"配置文件未找到: {filePath}");

        var yaml = LoadYaml(filePath);
        var root = EnsureMappingRoot(yaml);
        var skillsNode = EnsureChildMapping(root, "skills");

        var seqNode = new YamlSequenceNode();
        foreach (var folder in section.AdditionalFolders)
            seqNode.Add(new YamlScalarNode(folder));

        skillsNode.Children[new YamlScalarNode("additional_folders")] = seqNode;

        BackupAndSave(filePath, yaml);
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

    private static void EnsureFileExists(string filePath, string defaultContent)
    {
        if (File.Exists(filePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, defaultContent);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed class SystemConfigDto
{
    public AgentConfigSection Agent { get; set; } = new();
    public SkillsConfigSection Skills { get; set; } = new();
}

public sealed class AgentConfigSection
{
    public int SubAgentMaxDepth { get; set; } = 3;
}

public sealed class SkillsConfigSection
{
    public List<string> AdditionalFolders { get; set; } = [];
}
