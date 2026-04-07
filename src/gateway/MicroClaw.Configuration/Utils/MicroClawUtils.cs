namespace MicroClaw.Utils;
/// <summary>
/// MicroClaw 工具类集合。
/// </summary>
public static class MicroClawUtils
{
    /// <summary>
    /// 获取一个唯一字符串，通常用于标识 Session、Agent 实体等需要唯一标识的场景。
    /// </summary>
    /// <returns></returns>
    public static string GetUniqueId() => Guid.NewGuid().ToString("N");
}