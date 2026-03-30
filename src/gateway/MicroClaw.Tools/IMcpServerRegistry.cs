namespace MicroClaw.Tools;

/// <summary>
/// 运行时 MCP Server 注册表 — 维护应用进程内的 MCP Server 配置快照。
/// 在应用启动时从持久化存储同步，并随 API 增删改操作实时更新，无需重启即可感知变更。
/// </summary>
/// <remarks>
/// 与 <see cref="McpServerConfigStore"/>（持久化层）的区别：
/// <list type="bullet">
///   <item>注册表是进程内内存视图，不走数据库，查询零延迟；</item>
///   <item>ConfigStore 是持久化源，写操作须先更新 Store 再调用注册表，保证一致性；</item>
///   <item>注册表在启动时同步一次全量数据，后续依赖 API 调用方主动 Register/Unregister 维护。</item>
/// </list>
/// </remarks>
public interface IMcpServerRegistry
{
    /// <summary>注册或更新一个 MCP Server 配置（对应 Create / Update 操作）。</summary>
    void Register(McpServerConfig config);

    /// <summary>从注册表移除一个 MCP Server（对应 Delete 操作）。</summary>
    void Unregister(string serverId);

    /// <summary>返回注册表内所有 MCP Server 配置的只读快照（含已禁用条目）。</summary>
    IReadOnlyList<McpServerConfig> GetAll();

    /// <summary>返回注册表内所有 <see cref="McpServerConfig.IsEnabled"/> 为 true 的配置快照。</summary>
    IReadOnlyList<McpServerConfig> GetAllEnabled();

    /// <summary>按 ID 查找配置，不存在时返回 null。</summary>
    McpServerConfig? GetById(string serverId);
}
