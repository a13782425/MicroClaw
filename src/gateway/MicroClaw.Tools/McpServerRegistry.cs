using System.Collections.Concurrent;
using System.Text.Json;
using MicroClaw.Gateway.Contracts.Plugins;
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
    : BackgroundService, IMcpServerRegistry, IPluginMcpRegistrar
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

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

    // ── IPluginMcpRegistrar ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RegisterFromConfigFileAsync(string filePath, string pluginName, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            logger.LogWarning("Plugin MCP config file not found: {Path}", filePath);
            return;
        }

        string json = await File.ReadAllTextAsync(filePath, ct);
        using JsonDocument doc = JsonDocument.Parse(json);

        // Support both the standard { "mcpServers": { ... } } format and the
        // legacy root-level { "name": { ... } } format used by some plugin repos.
        JsonElement serversEl;
        if (doc.RootElement.TryGetProperty("mcpServers", out JsonElement standardServersEl))
        {
            serversEl = standardServersEl;
        }
        else if (IsLegacyServerMap(doc.RootElement))
        {
            serversEl = doc.RootElement;
            logger.LogWarning(
                "Plugin MCP config uses legacy root server format without 'mcpServers': {Path}",
                filePath);
        }
        else
        {
            logger.LogWarning("Plugin MCP config missing 'mcpServers' property: {Path}", filePath);
            return;
        }

        foreach (JsonProperty entry in serversEl.EnumerateObject())
        {
            string serverId = $"plugin:{pluginName}:{entry.Name}";
            try
            {
                McpServerConfig config = ParseMcpEntry(entry.Name, entry.Value, serverId, pluginName);
                // 同步写入持久化层（幂等 upsert），使插件 MCP 可在管理页展示
                store.Upsert(config);
                Register(config);
                logger.LogInformation("Registered plugin MCP server: {Name} (Id={Id}) from plugin '{Plugin}'",
                    config.Name, config.Id, pluginName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse MCP server '{Name}' from plugin '{Plugin}'", entry.Name, pluginName);
            }
        }
    }

    /// <inheritdoc/>
    public void UnregisterByPlugin(string pluginName)
    {
        string prefix = $"plugin:{pluginName}:";
        var toRemove = _servers.Keys.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (string id in toRemove)
            Unregister(id);

        // 同步删除持久化层中该插件的所有 MCP Server
        int deleted = store.DeleteByPluginId(pluginName);
        logger.LogInformation("Unregistered {Count} MCP servers from plugin '{Plugin}' (DB removed: {DbCount})",
            toRemove.Count, pluginName, deleted);
    }

    private static McpServerConfig ParseMcpEntry(string name, JsonElement el, string serverId, string pluginName)
    {
        // Determine transport type
        McpTransportType transport = McpTransportType.Stdio;
        if (el.TryGetProperty("type", out JsonElement typeEl))
        {
            string? typeStr = typeEl.GetString();
            transport = typeStr?.ToLowerInvariant() switch
            {
                "sse" => McpTransportType.Sse,
                "http" => McpTransportType.Http,
                _ => McpTransportType.Stdio,
            };
        }
        else if (el.TryGetProperty("url", out _))
        {
            transport = McpTransportType.Sse;
        }

        string? command = el.TryGetProperty("command", out JsonElement cmdEl) ? cmdEl.GetString() : null;

        IReadOnlyList<string>? args = null;
        if (el.TryGetProperty("args", out JsonElement argsEl))
            args = argsEl.Deserialize<string[]>(JsonOpts);

        IDictionary<string, string?>? env = null;
        if (el.TryGetProperty("env", out JsonElement envEl))
            env = envEl.Deserialize<Dictionary<string, string?>>(JsonOpts);

        string? url = el.TryGetProperty("url", out JsonElement urlEl2) ? urlEl2.GetString() : null;

        IDictionary<string, string>? headers = null;
        if (el.TryGetProperty("headers", out JsonElement headersEl))
            headers = headersEl.Deserialize<Dictionary<string, string>>(JsonOpts);

        return new McpServerConfig(
            Name: name,
            TransportType: transport,
            Command: command,
            Args: args,
            Env: env,
            Url: url,
            Headers: headers,
            Id: serverId,
            IsEnabled: true,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Source: McpServerSource.Plugin,
            PluginId: pluginName,
            PluginName: pluginName);
    }

    private static bool IsLegacyServerMap(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        bool hasEntries = false;
        foreach (JsonProperty entry in root.EnumerateObject())
        {
            hasEntries = true;
            if (entry.Value.ValueKind != JsonValueKind.Object)
                return false;

            if (!LooksLikeServerDefinition(entry.Value))
                return false;
        }

        return hasEntries;
    }

    private static bool LooksLikeServerDefinition(JsonElement element) =>
        element.TryGetProperty("command", out _) ||
        element.TryGetProperty("url", out _) ||
        element.TryGetProperty("type", out _) ||
        element.TryGetProperty("args", out _) ||
        element.TryGetProperty("env", out _) ||
        element.TryGetProperty("headers", out _);
}
