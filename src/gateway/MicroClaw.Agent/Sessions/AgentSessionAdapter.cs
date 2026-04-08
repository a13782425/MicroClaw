using System.Text.Json;
using MicroClaw.Abstractions.Sessions;
using Microsoft.Agents.AI;

namespace MicroClaw.Agent.Sessions;

/// <summary>
/// 将 MicroClaw <see cref="IMicroSession"/> 的元数据写入 AF <see cref="AgentSession.StateBag"/>，
/// 使中间件和 ContextProvider 能够通过标准 AF session 访问会话上下文。
/// </summary>
/// <remarks>
/// <para>
/// 设计原则：在 <see cref="AgentRunner"/> 调用 <c>chatAgent.CreateSessionAsync()</c> 创建 AF Session 后，
/// 通过 <see cref="PopulateStateBag"/> 将元数据注入 StateBag。
/// </para>
/// <para>
/// StateBag Key 命名约定：<c>mc.&lt;field&gt;</c>（使用 mc 前缀避免与 AF 内置 key 冲突）。
/// </para>
/// </remarks>
internal static class AgentSessionAdapter
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── StateBag Key 常量 ─────────────────────────────────────────────────

    internal const string KeySessionId = "mc.sessionId";
    internal const string KeyProviderId = "mc.providerId";
    internal const string KeyAgentId = "mc.agentId";
    internal const string KeyChannelType = "mc.channelType";
    internal const string KeyChannelId = "mc.channelId";
    internal const string KeyTitle = "mc.title";

    // ── 写入 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 将 <see cref="IMicroSession"/> 的字段写入 <see cref="AgentSessionStateBag"/>。
    /// </summary>
    public static void PopulateStateBag(AgentSessionStateBag bag, IMicroSession microSession)
    {
        bag.SetValue(KeySessionId, microSession.Id, JsonOpts);
        bag.SetValue(KeyProviderId, microSession.ProviderId, JsonOpts);
        bag.SetValue(KeyChannelType, microSession.ChannelType.ToString(), JsonOpts);
        bag.SetValue(KeyChannelId, microSession.ChannelId, JsonOpts);
        bag.SetValue(KeyTitle, microSession.Title, JsonOpts);

        if (microSession.AgentId is not null)
            bag.SetValue(KeyAgentId, microSession.AgentId, JsonOpts);
    }

    // ── 读取 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 从 <see cref="AgentSessionStateBag"/> 中读取字符串值。键不存在时返回 <c>null</c>。
    /// </summary>
    public static string? GetStringValue(AgentSessionStateBag bag, string key)
    {
        bag.TryGetValue<string>(key, out string? value, JsonOpts);
        return value;
    }
}
