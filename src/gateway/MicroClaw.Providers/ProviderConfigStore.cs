using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Providers;

public sealed class ProviderConfigStore(IDbContextFactory<GatewayDbContext> factory)
{
    public IReadOnlyList<ProviderConfig> All
    {
        get
        {
            using GatewayDbContext db = factory.CreateDbContext();
            return db.Providers
                .Select(e => ToConfig(e))
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// 返回当前设置为默认且已启用的 Provider；若无默认则返回第一个已启用的 Provider。
    /// </summary>
    public ProviderConfig? GetDefault()
    {
        using GatewayDbContext db = factory.CreateDbContext();
        return db.Providers
            .Where(p => p.IsEnabled)
            .OrderByDescending(p => p.IsDefault)
            .Select(e => ToConfig(e))
            .FirstOrDefault();
    }

    public ProviderConfig Add(ProviderConfig config)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        bool hasAny = db.Providers.Any();
        ProviderConfigEntity entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });
        // 第一个添加的模型自动设为默认
        if (!hasAny)
            entity.IsDefault = true;
        db.Providers.Add(entity);
        db.SaveChanges();
        return ToConfig(entity);
    }

    public ProviderConfig? Update(string id, ProviderConfig incoming)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        ProviderConfigEntity? entity = db.Providers.Find(id);
        if (entity is null) return null;

        entity.DisplayName = incoming.DisplayName;
        entity.Protocol = SerializeProtocol(incoming.Protocol);
        entity.BaseUrl = string.IsNullOrWhiteSpace(incoming.BaseUrl) ? null : incoming.BaseUrl;
        entity.ModelName = incoming.ModelName;
        entity.MaxOutputTokens = incoming.MaxOutputTokens;
        entity.IsEnabled = incoming.IsEnabled;
        entity.CapabilitiesJson = JsonSerializer.Serialize(incoming.Capabilities);

        if (!string.IsNullOrWhiteSpace(incoming.ApiKey) && incoming.ApiKey != "***")
            entity.ApiKey = incoming.ApiKey;

        db.SaveChanges();
        return ToConfig(entity);
    }

    public bool Delete(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        ProviderConfigEntity? entity = db.Providers.Find(id);
        if (entity is null) return false;
        db.Providers.Remove(entity);
        db.SaveChanges();
        return true;
    }

    public bool SetDefault(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        ProviderConfigEntity? target = db.Providers.Find(id);
        if (target is null) return false;
        foreach (ProviderConfigEntity e in db.Providers)
            e.IsDefault = e.Id == id;
        db.SaveChanges();
        return true;
    }

    private static ProviderConfig ToConfig(ProviderConfigEntity e) =>
        new()
        {
            Id = e.Id,
            DisplayName = e.DisplayName,
            Protocol = ParseProtocol(e.Protocol),
            BaseUrl = string.IsNullOrWhiteSpace(e.BaseUrl) ? null : e.BaseUrl,
            ApiKey = ResolveEnvVars(e.ApiKey) ?? string.Empty,
            ModelName = ResolveEnvVars(e.ModelName) ?? string.Empty,
            MaxOutputTokens = e.MaxOutputTokens,
            IsEnabled = e.IsEnabled,
            IsDefault = e.IsDefault,
            Capabilities = DeserializeCapabilities(e.CapabilitiesJson)
        };

    private static ProviderConfigEntity ToEntity(ProviderConfig c) =>
        new()
        {
            Id = c.Id,
            DisplayName = c.DisplayName,
            Protocol = SerializeProtocol(c.Protocol),
            BaseUrl = string.IsNullOrWhiteSpace(c.BaseUrl) ? null : c.BaseUrl,
            ApiKey = c.ApiKey,
            ModelName = c.ModelName,
            MaxOutputTokens = c.MaxOutputTokens,
            IsEnabled = c.IsEnabled,
            IsDefault = c.IsDefault,
            CapabilitiesJson = JsonSerializer.Serialize(c.Capabilities)
        };

    private static ProviderProtocol ParseProtocol(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "openai" => ProviderProtocol.OpenAI,
            // 历史数据兼容：openai-responses 静默降级为 OpenAI，SupportsResponsesApi 已移入 Capabilities
            "openai-responses" => ProviderProtocol.OpenAI,
            "anthropic" => ProviderProtocol.Anthropic,
            _ => ProviderProtocol.OpenAI
        };

    private static string SerializeProtocol(ProviderProtocol protocol) =>
        protocol switch
        {
            ProviderProtocol.OpenAI => "openai",
            ProviderProtocol.Anthropic => "anthropic",
            _ => "openai"
        };

    private static ProviderCapabilities DeserializeCapabilities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ProviderCapabilities();
        try { return JsonSerializer.Deserialize<ProviderCapabilities>(json) ?? new ProviderCapabilities(); }
        catch { return new ProviderCapabilities(); }
    }

    private static string? ResolveEnvVars(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Regex.Replace(value, @"\$\{([^}]+)\}", m =>
            Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }
}

