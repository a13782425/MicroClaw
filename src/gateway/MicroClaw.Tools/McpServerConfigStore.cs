using System.Text.Json;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Utils;

namespace MicroClaw.Tools;

/// <summary>
/// 全局 MCP Server 配置的 CRUD 存储，基于 MicroClaw 配置系统的 YAML 持久化。
/// </summary>
public sealed class McpServerConfigStore()
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly object _sync = new();

    public IReadOnlyList<McpServerConfig> All
        => GetOptions().Items.OrderBy(e => e.CreatedAtMs).Select(ToConfig).ToList().AsReadOnly();

    /// <summary>返回所有已启用的 MCP Server 配置。</summary>
    public IReadOnlyList<McpServerConfig> AllEnabled
        => GetOptions().Items.Where(e => e.IsEnabled).OrderBy(e => e.CreatedAtMs).Select(ToConfig).ToList().AsReadOnly();

    public McpServerConfig? GetById(string id)
        => GetOptions().Items.FirstOrDefault(e => e.Id == id) is { } entity ? ToConfig(entity) : null;

    public McpServerConfig Add(McpServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_sync)
        {
            McpServerConfig toAdd = config with
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };

            McpServerConfigEntity entity = ToEntity(toAdd);
            SaveOptions(new McpServersOptions
            {
                Items = [.. GetOptions().Items, entity]
            });

            return ToConfig(entity);
        }
    }

    public McpServerConfig? Update(string id, McpServerConfig incoming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(incoming);

        lock (_sync)
        {
            McpServersOptions options = GetOptions();
            McpServerConfigEntity? existing = options.Items.FirstOrDefault(e => e.Id == id);
            if (existing is null)
                return null;

            McpServerConfigEntity updated = new()
            {
                Id = existing.Id,
                Name = incoming.Name,
                TransportType = SerializeTransport(incoming.TransportType),
                Command = incoming.Command,
                ArgsJson = incoming.Args is not null ? JsonSerializer.Serialize(incoming.Args, JsonOpts) : null,
                EnvJson = incoming.Env is not null ? JsonSerializer.Serialize(incoming.Env, JsonOpts) : null,
                Url = incoming.Url,
                HeadersJson = incoming.Headers is not null ? JsonSerializer.Serialize(incoming.Headers, JsonOpts) : null,
                IsEnabled = incoming.IsEnabled,
                CreatedAtMs = existing.CreatedAtMs,
                Source = existing.Source,
                PluginId = existing.PluginId,
                PluginName = existing.PluginName,
            };

            SaveOptions(new McpServersOptions
            {
                Items = options.Items.Select(item => item.Id == id ? updated : item).ToList()
            });

            return ToConfig(updated);
        }
    }

    public bool Delete(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        lock (_sync)
        {
            McpServersOptions options = GetOptions();
            List<McpServerConfigEntity> remaining = options.Items.Where(item => item.Id != id).ToList();
            if (remaining.Count == options.Items.Count)
                return false;

            SaveOptions(new McpServersOptions { Items = remaining });
            return true;
        }
    }

    /// <summary>按 ID 列表返回已启用的 MCP Server 配置。</summary>
    public IReadOnlyList<McpServerConfig> GetEnabledByIds(IEnumerable<string> ids)
    {
        HashSet<string> idSet = ids.ToHashSet();
        return GetOptions().Items
            .Where(e => e.IsEnabled && idSet.Contains(e.Id))
            .Select(ToConfig)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>插入或更新一个 MCP Server 配置，主要用于插件注册。</summary>
    public McpServerConfig Upsert(McpServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_sync)
        {
            McpServersOptions options = GetOptions();
            McpServerConfigEntity? existing = options.Items.FirstOrDefault(item => item.Id == config.Id);
            McpServerConfigEntity result;

            if (existing is not null)
            {
                result = new McpServerConfigEntity
                {
                    Id = existing.Id,
                    Name = config.Name,
                    TransportType = SerializeTransport(config.TransportType),
                    Command = config.Command,
                    ArgsJson = config.Args is not null ? JsonSerializer.Serialize(config.Args, JsonOpts) : null,
                    EnvJson = config.Env is not null ? JsonSerializer.Serialize(config.Env, JsonOpts) : null,
                    Url = config.Url,
                    HeadersJson = config.Headers is not null ? JsonSerializer.Serialize(config.Headers, JsonOpts) : null,
                    IsEnabled = config.IsEnabled,
                    CreatedAtMs = existing.CreatedAtMs,
                    Source = (int)config.Source,
                    PluginId = config.PluginId,
                    PluginName = config.PluginName,
                };

                SaveOptions(new McpServersOptions
                {
                    Items = options.Items.Select(item => item.Id == config.Id ? result : item).ToList()
                });
            }
            else
            {
                McpServerConfig toAdd = config.CreatedAtUtc == default
                    ? config with { CreatedAtUtc = DateTimeOffset.UtcNow }
                    : config;
                result = ToEntity(toAdd);

                SaveOptions(new McpServersOptions
                {
                    Items = [.. options.Items, result]
                });
            }

            return ToConfig(result);
        }
    }

    /// <summary>按插件 ID 删除该插件注册的所有 MCP Server。</summary>
    public int DeleteByPluginId(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        lock (_sync)
        {
            McpServersOptions options = GetOptions();
            List<McpServerConfigEntity> remaining = options.Items.Where(item => item.PluginId != pluginId).ToList();
            int deletedCount = options.Items.Count - remaining.Count;
            if (deletedCount == 0)
                return 0;

            SaveOptions(new McpServersOptions { Items = remaining });
            return deletedCount;
        }
    }

    private static McpServerConfig ToConfig(McpServerConfigEntity e) => new(
        Id: e.Id,
        Name: e.Name,
        TransportType: ParseTransport(e.TransportType),
        Command: e.Command,
        Args: e.ArgsJson is not null
            ? JsonSerializer.Deserialize<List<string>>(e.ArgsJson)
            : null,
        Env: e.EnvJson is not null
            ? JsonSerializer.Deserialize<Dictionary<string, string?>>(e.EnvJson)
            : null,
        Url: e.Url,
        Headers: e.HeadersJson is not null
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(e.HeadersJson)
            : null,
        IsEnabled: e.IsEnabled,
        CreatedAtUtc: TimeUtils.FromMs(e.CreatedAtMs),
        Source: (McpServerSource)e.Source,
        PluginId: e.PluginId,
        PluginName: e.PluginName);

    private static McpServerConfigEntity ToEntity(McpServerConfig c) => new()
    {
        Id            = c.Id,
        Name          = c.Name,
        TransportType = SerializeTransport(c.TransportType),
        Command       = c.Command,
        ArgsJson      = c.Args is not null ? JsonSerializer.Serialize(c.Args, JsonOpts) : null,
        EnvJson       = c.Env is not null  ? JsonSerializer.Serialize(c.Env, JsonOpts)  : null,
        Url           = c.Url,
        HeadersJson   = c.Headers is not null ? JsonSerializer.Serialize(c.Headers, JsonOpts) : null,
        IsEnabled     = c.IsEnabled,
        CreatedAtMs   = TimeUtils.ToMs(c.CreatedAtUtc),
        Source        = (int)c.Source,
        PluginId      = c.PluginId,
        PluginName    = c.PluginName,
    };

    private static McpTransportType ParseTransport(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "sse"  => McpTransportType.Sse,
            "http" => McpTransportType.Http,
            _      => McpTransportType.Stdio,
        };

    private static string SerializeTransport(McpTransportType t) =>
        t switch
        {
            McpTransportType.Sse  => "sse",
            McpTransportType.Http => "http",
            _                     => "stdio",
        };

    private static McpServersOptions GetOptions() => MicroClawConfig.Get<McpServersOptions>();

    private static void SaveOptions(McpServersOptions options) => MicroClawConfig.Save(options);
}
