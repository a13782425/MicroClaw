using MicroClaw.Abstractions.Channel;
using MicroClaw.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace MicroClaw.Channels.Feishu;

/// <summary>飞书渠道 DI 注册扩展，封装所有内部实现类的注册，避免外部项目引用飞书具体类型。</summary>
public static class FeishuChannelExtensions
{
    /// <summary>
    /// 注册飞书渠道所需的全部服务，包括消息处理器、WebSocket 管理器、工具工厂等。
    /// </summary>
    public static IServiceCollection AddFeishuChannel(this IServiceCollection services)
    {
        // 内部基础设施（全部 internal，在程序集外不可见）
        services.AddSingleton<FeishuTokenCache>();
        services.AddSingleton<FeishuRateLimiter>();
        services.AddSingleton<FeishuChannelHealthStore>();
        services.AddSingleton<FeishuChannelStatsService>();
        services.AddSingleton<FeishuMessageProcessor>();

        // WebSocket 长连接管理器：同时注册为单例和 IHostedService，并暴露同步接口
        services.AddSingleton<FeishuWebSocketManager>();
        services.AddHostedService(sp => sp.GetRequiredService<FeishuWebSocketManager>());
        services.AddSingleton<IFeishuWebSocketSync>(sp => sp.GetRequiredService<FeishuWebSocketManager>());

        // Channel Provider（对外通过 IChannelProvider 暴露）
        services.AddSingleton<FeishuChannelProvider>();
        services.AddSingleton<IChannelProvider>(sp => sp.GetRequiredService<FeishuChannelProvider>());

        // 工具工厂（对外通过 IToolProvider 暴露）
        services.AddSingleton<FeishuToolsFactory>();
        services.AddSingleton<IToolProvider>(sp => sp.GetRequiredService<FeishuToolsFactory>());

        // 消息处理器暴露重试接口
        services.AddSingleton<IFeishuRetryProcessor>(sp => sp.GetRequiredService<FeishuMessageProcessor>());

        return services;
    }
}
