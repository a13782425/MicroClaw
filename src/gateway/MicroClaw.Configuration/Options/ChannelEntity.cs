using System.Text.Json.Serialization;
namespace MicroClaw.Configuration.Options;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChannelType
{
    [JsonStringEnumMemberName("web")]
    Web,
    
    [JsonStringEnumMemberName("feishu")]
    Feishu,
    
    [JsonStringEnumMemberName("wecom")]
    WeCom,
    
    [JsonStringEnumMemberName("wechat")]
    WeChat
}

/// <summary>
/// 渠道配置实体类，用于持久化存储和传输。
/// </summary>
public sealed class ChannelEntity
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [YamlMember(Alias = "channel_type")]
    public ChannelType ChannelType { get; set; }

    [YamlMember(Alias = "is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [YamlMember(Alias = "setting_json")]
    public string SettingJson { get; set; } = "{}";
}