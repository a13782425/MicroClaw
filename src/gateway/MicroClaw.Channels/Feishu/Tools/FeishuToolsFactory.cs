using MicroClaw.Gateway.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-C-1/F-C-3/F-C-4/F-C-5: 飞书工具工厂 — 从已启用的飞书渠道配置中读取凭据，创建 Agent 可调用的飞书工具集
/// （包含文档读取/写入、多维表格读取/写入、知识库搜索等工具）。注册为单例，在 AgentRunner 加载工具时按需调用。
/// </summary>
public sealed class FeishuToolsFactory(
    ChannelConfigStore configStore,
    ILogger<FeishuToolsFactory> logger)
{
    /// <summary>
    /// 返回所有可用飞书工具的元数据描述（不依赖渠道配置）。
    /// </summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        [.. FeishuDocTools.GetToolDescriptions(), .. FeishuBitableTools.GetToolDescriptions(), .. FeishuWikiTools.GetToolDescriptions(), .. FeishuCalendarTools.GetToolDescriptions(), .. FeishuApprovalTools.GetToolDescriptions()];

    /// <summary>
    /// 使用第一个已启用飞书渠道的凭据创建工具实例列表。
    /// 若无已启用的飞书渠道配置或凭据缺失，则返回空列表。
    /// </summary>
    public IReadOnlyList<AIFunction> CreateTools()
    {
        ChannelConfig? config = configStore
            .GetByType(ChannelType.Feishu)
            .FirstOrDefault(c => c.IsEnabled);

        if (config is null)
        {
            logger.LogDebug("未找到已启用的飞书渠道配置，跳过飞书工具注册");
            return [];
        }

        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(config.SettingsJson) ?? new();

        if (string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
        {
            logger.LogWarning("飞书渠道 {ChannelId} AppId/AppSecret 未配置，跳过飞书工具注册", config.Id);
            return [];
        }

        return [.. FeishuDocTools.CreateTools(settings, logger), .. FeishuBitableTools.CreateTools(settings, logger), .. FeishuBitableTools.CreateWriteTools(settings, logger), .. FeishuWikiTools.CreateTools(settings, logger), .. FeishuCalendarTools.CreateTools(settings, logger), .. FeishuApprovalTools.CreateTools(settings, logger)];
    }
}
