using MicroClaw.Configuration.Options;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-1/F-C-3/F-C-4/F-C-5: 飞书工具工厂 — 实现 <see cref="IToolProvider"/>（Category=Channel），
/// 按指定渠道配置中的凭据创建 Agent 可调用的飞书工具集。
/// 注册为单例，供 ToolCollector 按渠道类型动态注入工具。
/// </summary>
public sealed class FeishuToolsFactory(
    ChannelConfigStore channelConfigStore,
    ILogger<FeishuToolsFactory> logger) : IToolProvider
{
    // ── IToolProvider ──────────────────────────────────────────────────────

    public ToolCategory Category => ToolCategory.Channel;
    public string GroupId => "feishu";
    public string DisplayName => "飞书";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions()
        => GetStaticToolDescriptions();

    public Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default)
    {
        if (context.ChannelType != ChannelType.Feishu || string.IsNullOrWhiteSpace(context.ChannelId))
            return Task.FromResult(ToolProviderResult.Empty);

        ChannelEntity? config = channelConfigStore.GetById(context.ChannelId);
        if (config is null)
            return Task.FromResult(ToolProviderResult.Empty);

        IReadOnlyList<AIFunction> tools = CreateToolsFromConfig(config);
        return Task.FromResult(new ToolProviderResult(tools));
    }

    // ── 内部实现 ──────────────────────────────────────────────────────────

    private IReadOnlyList<AIFunction> CreateToolsFromConfig(ChannelEntity config)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(config.SettingJson) ?? new();

        if (string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
        {
            logger.LogWarning("飞书渠道 {ChannelId} AppId/AppSecret 未配置，跳过飞书工具注册", config.Id);
            return [];
        }

        return [.. FeishuDocTools.CreateTools(settings, logger), .. FeishuBitableTools.CreateTools(settings, logger), .. FeishuBitableTools.CreateWriteTools(settings, logger), .. FeishuWikiTools.CreateTools(settings, logger), .. FeishuCalendarTools.CreateTools(settings, logger), .. FeishuApprovalTools.CreateTools(settings, logger)];
    }

    /// <summary>返回所有可用飞书工具的元数据描述（不依赖渠道配置）。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetStaticToolDescriptions() =>
        [.. FeishuDocTools.GetToolDescriptions(), .. FeishuBitableTools.GetToolDescriptions(), .. FeishuWikiTools.GetToolDescriptions(), .. FeishuCalendarTools.GetToolDescriptions(), .. FeishuApprovalTools.GetToolDescriptions()];
}
