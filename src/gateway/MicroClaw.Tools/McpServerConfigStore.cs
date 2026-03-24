using System.Text.Json;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Tools;

/// <summary>
/// 全局 MCP Server 配置的 CRUD 存储，基于 EF Core。
/// </summary>
public sealed class McpServerConfigStore(IDbContextFactory<GatewayDbContext> factory)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public IReadOnlyList<McpServerConfig> All
    {
        get
        {
            using GatewayDbContext db = factory.CreateDbContext();
            return db.McpServers.Select(ToConfig).ToList()
                .OrderBy(s => s.CreatedAtUtc).ToList().AsReadOnly();
        }
    }

    /// <summary>返回所有 IsEnabled=true 的 MCP Server，用于空 EnabledMcpServerIds 时的默认全量加载。</summary>
    public IReadOnlyList<McpServerConfig> AllEnabled
    {
        get
        {
            using GatewayDbContext db = factory.CreateDbContext();
            return db.McpServers.Where(e => e.IsEnabled).Select(ToConfig).ToList()
                .OrderBy(s => s.CreatedAtUtc).ToList().AsReadOnly();
        }
    }

    public McpServerConfig? GetById(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        McpServerConfigEntity? entity = db.McpServers.Find(id);
        return entity is null ? null : ToConfig(entity);
    }

    public McpServerConfig Add(McpServerConfig config)
    {
        McpServerConfigEntity entity = ToEntity(config with
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        using GatewayDbContext db = factory.CreateDbContext();
        db.McpServers.Add(entity);
        db.SaveChanges();
        return ToConfig(entity);
    }

    public McpServerConfig? Update(string id, McpServerConfig incoming)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        McpServerConfigEntity? entity = db.McpServers.Find(id);
        if (entity is null) return null;

        entity.Name          = incoming.Name;
        entity.TransportType = SerializeTransport(incoming.TransportType);
        entity.Command       = incoming.Command;
        entity.ArgsJson      = incoming.Args is not null
            ? JsonSerializer.Serialize(incoming.Args, JsonOpts) : null;
        entity.EnvJson       = incoming.Env is not null
            ? JsonSerializer.Serialize(incoming.Env, JsonOpts) : null;
        entity.Url           = incoming.Url;
        entity.HeadersJson   = incoming.Headers is not null
            ? JsonSerializer.Serialize(incoming.Headers, JsonOpts) : null;
        entity.IsEnabled     = incoming.IsEnabled;

        db.SaveChanges();
        return ToConfig(entity);
    }

    public bool Delete(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        McpServerConfigEntity? entity = db.McpServers.Find(id);
        if (entity is null) return false;
        db.McpServers.Remove(entity);
        db.SaveChanges();
        return true;
    }

    /// <summary>按 ID 列表返回启用的 MCP Server 配置（用于 Agent 引用过滤）。</summary>
    public IReadOnlyList<McpServerConfig> GetEnabledByIds(IEnumerable<string> ids)
    {
        HashSet<string> idSet = ids.ToHashSet();
        using GatewayDbContext db = factory.CreateDbContext();
        return db.McpServers
            .Where(e => e.IsEnabled && idSet.Contains(e.Id))
            .Select(ToConfig)
            .ToList()
            .AsReadOnly();
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
        CreatedAtUtc: e.CreatedAtUtc);

    private static McpServerConfigEntity ToEntity(McpServerConfig c) => new()
    {
        Id           = c.Id,
        Name         = c.Name,
        TransportType = SerializeTransport(c.TransportType),
        Command      = c.Command,
        ArgsJson     = c.Args is not null ? JsonSerializer.Serialize(c.Args, JsonOpts) : null,
        EnvJson      = c.Env is not null  ? JsonSerializer.Serialize(c.Env, JsonOpts)  : null,
        Url          = c.Url,
        HeadersJson  = c.Headers is not null ? JsonSerializer.Serialize(c.Headers, JsonOpts) : null,
        IsEnabled    = c.IsEnabled,
        CreatedAtUtc = c.CreatedAtUtc,
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
