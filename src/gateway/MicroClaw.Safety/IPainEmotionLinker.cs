namespace MicroClaw.Safety;

/// <summary>
/// 痛觉-情绪联动接口：当痛觉记忆被记录时，通知情绪系统做出相应响应。
/// <para>
/// 接口定义在 <c>MicroClaw.Safety</c>，实现位于 <c>MicroClaw.Agent</c>，
/// 以保持两个模块间的解耦（Safety 不引用 Emotion）。
/// </para>
/// <para>
/// 只有严重度达到 <see cref="PainSeverity.High"/> 或以上时，联动实现才应触发情绪变化。
/// </para>
/// </summary>
public interface IPainEmotionLinker
{
    /// <summary>
    /// 痛觉记忆已被记录时调用，令情绪系统根据严重度做出相应调整。
    /// </summary>
    /// <param name="memory">刚刚保存的痛觉记忆（含 AgentId 和 Severity）。</param>
    /// <param name="ct">取消令牌。</param>
    Task LinkAsync(PainMemory memory, CancellationToken ct = default);
}
