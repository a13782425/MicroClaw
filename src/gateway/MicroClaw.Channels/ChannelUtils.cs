using MicroClaw.Configuration.Options;
namespace MicroClaw.Channels;
public static class ChannelUtils
{
    /// <summary>内置 Web Channel 的固定 ID。</summary>
    public const string WebChannelId = "web";
    
    public static ChannelType ParseChannelType(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "web" => ChannelType.Web,
            "feishu" => ChannelType.Feishu,
            "wecom" => ChannelType.WeCom,
            "wechat" => ChannelType.WeChat,
            _ => ChannelType.Web
        };
    
    public static string SerializeChannelType(ChannelType type) =>
        type switch
        {
            ChannelType.Web => "web",
            ChannelType.Feishu => "feishu",
            ChannelType.WeCom => "wecom",
            ChannelType.WeChat => "wechat",
            _ => "web"
        };
}