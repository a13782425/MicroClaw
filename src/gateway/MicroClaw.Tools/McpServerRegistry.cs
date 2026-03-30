using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Tools;

/// <summary>
/// <see cref="IMcpServerRegistry"/> 默认实现 — 进程内 <see cref="ConcurrentDictionary{TKey,TValue}"/> 缓存。
/// </summary>
/// <remarks>
/// 作为 <see cref="IHostedService"/> 注册时，会在应用启动时从 <see cref="McpServerConfigStore"/> 全量同步配置
///（含已禁用条目），后续通过 <see cref="Register"/>/<see cref="Unregister"/> 实时维护，始终与 Store 保持一致。
/// </remarks>
public sealed class McpServerRegistry(McpServerConfigStore store, ILogger<McpServerRegistry> logger)
    : BackgroundService, IMcpServerRegistry
{
    private readonly ConcurrentDictionary<string, McpServerConfig> _servers = new();

    // ── IHostedService 启动时同步 ─────────────────────────────────────────────

    /// <summary>
    /// 覆盖 StartAsync，在后台任务启动前同步加载数据，确保 <see cref="StartAsync"/> 返回时注册表已就绪。
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<McpServerConfig> all = store.All;
        foreach (McpServerConfig cfg in all)
            _servers[cfg.Id] = cfg;

        logger.LogInformation("MCP 注册表初始化完成，已加载 {Count} 个服务器配置", all.Count);
        return base.StartAsync(cancellationToken);
    }

    /// <summary>长驻任务（无需持续运行，初始化在 <see cref="StartAsync"/> 中完成）。</summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    // ── IMcpServerRegistry ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Register(McpServerConfig config)
    {
        _servers[config.Id] = config;
        logger.LogDebug(
            "MCP 注册表：注册服务器 {Name} (Id={Id}, Enabled={Enabled})",
            config.Name, config.Id, config.IsEnabled);
    }

    /// <inheritdoc/>
    public void Unregister(string serverId)
    {
        if (_servers.TryRemove(serverId, out McpServerConfig? removed))
            logger.LogDebug("MCP 注册表：移除服务器 {Name} (Id={Id})", removed.Name, serverId);
    }

    /// <inheritdoc/>
    public IReadOnlyList<McpServerConfig> GetAll() =>
        _servers.Values.OrderBy(s => s.CreatedAtUtc).ToList().AsReadOnly();

    /// <inheritdoc/>
    public IReadOnlyList<McpServerConfig> GetAllEnabled() =>
        _servers.Values.Where(s => s.IsEnabled).OrderBy(s => s.CreatedAtUtc).ToList().AsReadOnly();

    /// <inheritdoc/>
    public McpServerConfig? GetById(string serverId) =>
        _servers.TryGetValue(serverId, out McpServerConfig? cfg) ? cfg : null;
}
