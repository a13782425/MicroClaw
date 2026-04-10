namespace MicroClaw.Abstractions.Channel;

/// <summary>
/// 渠道诊断信息快照，由 <see cref="IChannelProvider.GetDiagnosticsAsync"/> 返回。
/// 通用字段适用于所有渠道类型；渠道特有数据通过 <see cref="Extra"/> 字典扩展。
/// </summary>
public sealed record ChannelDiagnostics(
    string ChannelId,
    string ChannelType,
    string Status,
    IReadOnlyDictionary<string, object?> Extra)
{
    /// <summary>构建一个基础 ok 状态的诊断信息（不含额外数据）。</summary>
    public static ChannelDiagnostics Ok(string channelId, string channelType)
        => new(channelId, channelType, "ok", new Dictionary<string, object?>());
}
