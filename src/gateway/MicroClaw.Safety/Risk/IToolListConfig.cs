namespace MicroClaw.Safety;

/// <summary>
/// 工具调用白名单/灰名单配置接口。
/// <list type="bullet">
///   <item><description>白名单：列表中的工具绕过全部风险检查，始终允许执行。</description></item>
///   <item><description>灰名单：列表中的工具在执行前被阻止，并提示需要用户明确确认。</description></item>
///   <item><description>不在任何列表中的工具按默认拦截器逻辑处理。</description></item>
/// </list>
/// </summary>
public interface IToolListConfig
{
    /// <summary>
    /// 判断指定工具是否处于白名单（免检）。
    /// </summary>
    /// <param name="toolName">工具名称（大小写不敏感）。</param>
    bool IsWhitelisted(string toolName);

    /// <summary>
    /// 判断指定工具是否处于灰名单（需确认）。
    /// </summary>
    /// <param name="toolName">工具名称（大小写不敏感）。</param>
    bool IsGreylisted(string toolName);

    /// <summary>获取所有白名单工具名称（大小写规范化后）。</summary>
    IReadOnlyCollection<string> WhitelistedTools { get; }

    /// <summary>获取所有灰名单工具名称（大小写规范化后）。</summary>
    IReadOnlyCollection<string> GreylistedTools { get; }
}
