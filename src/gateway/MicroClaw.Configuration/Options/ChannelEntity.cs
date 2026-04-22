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
    /// <summary>
    /// 渠道实例的唯一标识。
    /// </summary>
    [YamlMember(Alias = "id", Description = "渠道实例的唯一标识。")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 渠道实例的展示名称。
    /// </summary>
    [YamlMember(Alias = "display_name", Description = "渠道实例的展示名称。")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 渠道类型，例如 Web、飞书、企微或微信。
    /// </summary>
    [YamlMember(Alias = "channel_type", Description = "渠道类型，例如 Web、飞书、企微或微信。")]
    public ChannelType ChannelType { get; set; }

    /// <summary>
    /// 指示该渠道是否启用。
    /// </summary>
    [YamlMember(Alias = "is_enabled", Description = "指示该渠道是否启用。")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 渠道特定配置，使用 JSON 字符串持久化。
    /// </summary>
    [YamlMember(Alias = "setting_json", Description = "渠道特定配置，使用 JSON 字符串持久化。")]
    public string SettingJson { get; set; } = "{}";
}