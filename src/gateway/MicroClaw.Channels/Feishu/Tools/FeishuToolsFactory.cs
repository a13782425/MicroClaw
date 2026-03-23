using MicroClaw.Gateway.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-1/F-C-3/F-C-4/F-C-5: 飞书工具工厂 — 实现 <see cref="IChannelToolProvider"/>，
/// 按指定渠道配置中的凭据创建 Agent 可调用的飞书工具集。
/// 注册为单例，同时注册为 IChannelToolProvider，供 AgentRunner 按渠道类型动态注入工具。
/// </summary>
public sealed class FeishuToolsFactory(
    ILogger<FeishuToolsFactory> logger) : IChannelToolProvider
{
    // ── IChannelToolProvider ────────────────────────────────────────────────

    public ChannelType ChannelType => ChannelType.Feishu;

    IReadOnlyList<(string Name, string Description)> IChannelToolProvider.GetToolDescriptions()
        => GetToolDescriptions();

    public IReadOnlyList<AIFunction> CreateToolsForChannel(ChannelConfig config)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(config.SettingsJson) ?? new();

        if (string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
        {
            logger.LogWarning("飞书渠道 {ChannelId} AppId/AppSecret 未配置，跳过飞书工具注册", config.Id);
            return [];
        }

        return [.. FeishuDocTools.CreateTools(settings, logger), .. FeishuBitableTools.CreateTools(settings, logger), .. FeishuBitableTools.CreateWriteTools(settings, logger), .. FeishuWikiTools.CreateTools(settings, logger), .. FeishuCalendarTools.CreateTools(settings, logger), .. FeishuApprovalTools.CreateTools(settings, logger)];
    }

    // ── 静态工具描述（不依赖渠道配置，供 UI 展示）──────────────────────────────

    /// <summary>返回所有可用飞书工具的元数据描述（不依赖渠道配置）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        [.. FeishuDocTools.GetToolDescriptions(), .. FeishuBitableTools.GetToolDescriptions(), .. FeishuWikiTools.GetToolDescriptions(), .. FeishuCalendarTools.GetToolDescriptions(), .. FeishuApprovalTools.GetToolDescriptions()];
}
