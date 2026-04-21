using Microsoft.Extensions.Configuration;
using MicroClaw.Configuration;
using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Agent.Workflows;

/// <summary>
/// 工作流配置列表。
/// 通过 <c>workflows.yaml</c> 持久化，通过 <see cref="MicroClawConfig.Get{T}"/> 读取，
/// 通过 <see cref="MicroClawConfig.Save{T}(T)"/> 写回。
/// </summary>
[MicroClawYamlConfig("workflows", FileName = "workflows.yaml", IsWritable = true)]
public sealed class WorkflowsOptions : IMicroClawConfigOptions
{
    [ConfigurationKeyName("items")]
    public List<WorkflowConfigEntity> Items { get; set; } = [];
}