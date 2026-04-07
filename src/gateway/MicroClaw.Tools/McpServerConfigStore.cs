using System.Text.Json;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Utils;

namespace MicroClaw.Tools;

/// <summary>
/// 鍏ㄥ眬 MCP Server 閰嶇疆鐨?CRUD 瀛樺偍锛屽熀浜?YAML 鏂囦欢锛堝唴瀛樼紦瀛?+ 鍐欐椂钀界洏锛夈€?
/// </summary>
public sealed class McpServerConfigStore(string configDir)
    : YamlFileStore<McpServerConfigEntity>(Path.Combine(configDir, "mcp-servers.yaml"), e => e.Id)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public IReadOnlyList<McpServerConfig> All
        => GetAll().OrderBy(e => e.CreatedAtMs).Select(ToConfig).ToList().AsReadOnly();

    /// <summary>杩斿洖鎵€鏈?IsEnabled=true 鐨?MCP Server锛岀敤浜庣┖ EnabledMcpServerIds 鏃剁殑榛樿鍏ㄩ噺鍔犺浇銆?/summary>
    public IReadOnlyList<McpServerConfig> AllEnabled
        => GetAll().Where(e => e.IsEnabled).OrderBy(e => e.CreatedAtMs).Select(ToConfig).ToList().AsReadOnly();

    public McpServerConfig? GetById(string id)
        => GetYamlById(id) is { } e ? ToConfig(e) : null;

    public McpServerConfig Add(McpServerConfig config)
    {
        McpServerConfig toAdd = config with
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        McpServerConfigEntity entity = ToEntity(toAdd);
        SetYaml(entity);
        return ToConfig(entity);
    }

    public McpServerConfig? Update(string id, McpServerConfig incoming)
    {
        var updated = MutateYaml(id, e =>
        {
            e.Name          = incoming.Name;
            e.TransportType = SerializeTransport(incoming.TransportType);
            e.Command       = incoming.Command;
            e.ArgsJson      = incoming.Args is not null ? JsonSerializer.Serialize(incoming.Args, JsonOpts) : null;
            e.EnvJson       = incoming.Env is not null  ? JsonSerializer.Serialize(incoming.Env, JsonOpts)  : null;
            e.Url           = incoming.Url;
            e.HeadersJson   = incoming.Headers is not null ? JsonSerializer.Serialize(incoming.Headers, JsonOpts) : null;
            e.IsEnabled     = incoming.IsEnabled;
        });
        return updated is null ? null : ToConfig(updated);
    }

    public bool Delete(string id) => RemoveYaml(id);

    /// <summary>鎸?ID 鍒楄〃杩斿洖鍚敤鐨?MCP Server 閰嶇疆锛堢敤浜?Agent 寮曠敤杩囨护锛夈€?/summary>
    public IReadOnlyList<McpServerConfig> GetEnabledByIds(IEnumerable<string> ids)
    {
        HashSet<string> idSet = ids.ToHashSet();
        return GetAll()
            .Where(e => e.IsEnabled && idSet.Contains(e.Id))
            .Select(ToConfig)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>鎻掑叆鎴栨洿鏂颁竴涓?MCP Server 閰嶇疆锛堜富瑕佺敤浜庢彃浠舵敞鍐岋級銆?/summary>
    public McpServerConfig Upsert(McpServerConfig config)
    {
        McpServerConfigEntity? result = null;
        ExecuteWrite(items =>
        {
            if (items.TryGetValue(config.Id, out McpServerConfigEntity? existing))
            {
                existing.Name          = config.Name;
                existing.TransportType = SerializeTransport(config.TransportType);
                existing.Command       = config.Command;
                existing.ArgsJson      = config.Args is not null ? JsonSerializer.Serialize(config.Args, JsonOpts) : null;
                existing.EnvJson       = config.Env is not null  ? JsonSerializer.Serialize(config.Env, JsonOpts)  : null;
                existing.Url           = config.Url;
                existing.HeadersJson   = config.Headers is not null ? JsonSerializer.Serialize(config.Headers, JsonOpts) : null;
                existing.IsEnabled     = config.IsEnabled;
                existing.Source        = (int)config.Source;
                existing.PluginId      = config.PluginId;
                existing.PluginName    = config.PluginName;
                result = existing;
            }
            else
            {
                McpServerConfig toAdd = config.CreatedAtUtc == default
                    ? config with { CreatedAtUtc = DateTimeOffset.UtcNow }
                    : config;
                McpServerConfigEntity entity = ToEntity(toAdd);
                items[entity.Id] = entity;
                result = entity;
            }
            return true;
        });
        return ToConfig(result!);
    }

    /// <summary>鎸夋彃浠?ID 鍒犻櫎璇ユ彃浠舵敞鍐岀殑鎵€鏈?MCP Server銆?/summary>
    public int DeleteByPluginId(string pluginId) =>
        RemoveAllYaml(e => e.PluginId == pluginId);

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
}
