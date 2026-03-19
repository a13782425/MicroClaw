using System.Text.Json.Serialization;

namespace MicroClaw.Gateway.Contracts;

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
