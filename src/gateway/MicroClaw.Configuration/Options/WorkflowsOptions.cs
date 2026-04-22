using YamlDotNet.Serialization;

namespace MicroClaw.Configuration.Options;

/// <summary>
/// 工作流配置列表。
/// 通过 <c>workflows.yaml</c> 持久化，通过 <see cref="MicroClawConfig.Get{T}"/> 读取，
/// 通过 <see cref="MicroClawConfig.Save{T}(T)"/> 写回。
/// </summary>
[MicroClawYamlConfig("workflows", FileName = "workflows.yaml", IsWritable = true)]
public sealed class WorkflowsOptions : IMicroClawConfigTemplate
{
    /// <summary>
    /// 当前系统中持久化的工作流配置列表。
    /// </summary>
    [YamlMember(Alias = "items", Description = "当前系统中持久化的工作流配置列表。")]
    public List<WorkflowConfigEntity> Items { get; set; } = [];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new WorkflowsOptions();
}