
namespace MicroClaw.Configuration;

/// <summary>
/// RAG 自动遗忘相关配置，映射到配置节 rag。
/// </summary>
[MicroClawYamlConfig("rag", FileName = "rag.yaml", IsWritable = true)]
public sealed class RagOptions : IMicroClawConfigTemplate
{
    /// <summary>
    /// 每个会话 RAG 数据库允许占用的最大存储空间，单位为 MB。
    /// 超过后会优先清理命中次数较低的片段。
    /// </summary>
    [YamlMember(Alias = "max_storage_size_mb", Description = "每个会话 RAG 数据库允许占用的最大存储空间，单位为 MB。")]
    public double MaxStorageSizeMb { get; set; } = 50;

    /// <summary>
    /// 触发清理后的目标容量比例，相对于最大容量取值，范围为 0.0 到 1.0。
    /// </summary>
    [YamlMember(Alias = "prune_target_percent", Description = "触发清理后的目标容量比例，相对于最大容量取值。")]
    public double PruneTargetPercent { get; set; } = 0.8;

    public IMicroClawConfigOptions CreateDefaultTemplate() => new RagOptions();
}
