using MicroClaw.Gateway.Contracts;
using Microsoft.Extensions.AI;

namespace MicroClaw.Channels;

/// <summary>
/// 渠道工具提供者接口 — 插件渠道通过实现此接口并注册到 DI（AddSingleton&lt;IChannelToolProvider, MyToolProvider&gt;），
/// 即可向 AgentRunner 注入与该渠道关联的专属工具，无需修改核心代码。
/// </summary>
public interface IChannelToolProvider
{
    /// <summary>此提供者对应的渠道类型。</summary>
    ChannelType ChannelType { get; }

    /// <summary>返回工具的元数据描述列表（不需要渠道凭据，用于 UI 展示）。</summary>
    IReadOnlyList<(string Name, string Description)> GetToolDescriptions();

    /// <summary>基于指定渠道配置（含凭据信息）创建可调用的 AIFunction 工具实例列表。</summary>
    IReadOnlyList<AIFunction> CreateToolsForChannel(ChannelConfig config);
}
