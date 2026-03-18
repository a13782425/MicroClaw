using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Channels;

public sealed class ChannelConfigStore(IDbContextFactory<GatewayDbContext> factory)
{
    public IReadOnlyList<ChannelConfig> All
    {
        get
        {
            using GatewayDbContext db = factory.CreateDbContext();
            return db.Channels
                .Select(e => ToConfig(e))
                .ToList()
                .AsReadOnly();
        }
    }

    public ChannelConfig? GetById(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        ChannelConfigEntity? entity = db.Channels.Find(id);
        return entity is null ? null : ToConfig(entity);
    }

    public IReadOnlyList<ChannelConfig> GetByType(ChannelType type)
    {
        string typeStr = SerializeChannelType(type);
        using GatewayDbContext db = factory.CreateDbContext();
        return db.Channels
            .Where(e => e.ChannelType == typeStr)
            .Select(e => ToConfig(e))
            .ToList()
            .AsReadOnly();
    }

    public ChannelConfig Add(ChannelConfig config)
    {
        ChannelConfigEntity entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });
        using GatewayDbContext db = factory.CreateDbContext();
        db.Channels.Add(entity);
        db.SaveChanges();
        return ToConfig(entity);
    }

    public ChannelConfig? Update(string id, ChannelConfig incoming)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        ChannelConfigEntity? entity = db.Channels.Find(id);
        if (entity is null) return null;

        entity.DisplayName = incoming.DisplayName;
        entity.ChannelType = SerializeChannelType(incoming.ChannelType);
        entity.ProviderId = incoming.ProviderId;
        entity.IsEnabled = incoming.IsEnabled;

        // 如果 settings 中包含掩码密钥，保留原值
        entity.SettingsJson = MergeSettings(entity.SettingsJson, incoming.SettingsJson, incoming.ChannelType);

        db.SaveChanges();
        return ToConfig(entity);
    }

    public bool Delete(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        ChannelConfigEntity? entity = db.Channels.Find(id);
        if (entity is null) return false;
        db.Channels.Remove(entity);
        db.SaveChanges();
        return true;
    }

    private static string MergeSettings(string? existingJson, string incomingJson, ChannelType type)
    {
        if (type != ChannelType.Feishu) return incomingJson;

        FeishuChannelSettings? existing = DeserializeFeishuSettings(existingJson);
        FeishuChannelSettings? incoming = DeserializeFeishuSettings(incomingJson);
        if (existing is null || incoming is null) return incomingJson;

        // 如果 AppSecret 是掩码值，保留原始值
        string appSecret = (string.IsNullOrWhiteSpace(incoming.AppSecret) || incoming.AppSecret == "***")
            ? existing.AppSecret
            : incoming.AppSecret;

        FeishuChannelSettings merged = incoming with { AppSecret = appSecret };
        return JsonSerializer.Serialize(merged);
    }

    private static FeishuChannelSettings? DeserializeFeishuSettings(string? json) =>
        FeishuChannelSettings.TryParse(json);

    private static ChannelConfig ToConfig(ChannelConfigEntity e) =>
        new()
        {
            Id = e.Id,
            DisplayName = e.DisplayName,
            ChannelType = ParseChannelType(e.ChannelType),
            ProviderId = e.ProviderId,
            IsEnabled = e.IsEnabled,
            SettingsJson = ResolveEnvVars(e.SettingsJson) ?? "{}"
        };

    private static ChannelConfigEntity ToEntity(ChannelConfig c) =>
        new()
        {
            Id = c.Id,
            DisplayName = c.DisplayName,
            ChannelType = SerializeChannelType(c.ChannelType),
            ProviderId = c.ProviderId,
            IsEnabled = c.IsEnabled,
            SettingsJson = c.SettingsJson
        };

    public static ChannelType ParseChannelType(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "web" => ChannelType.Web,
            "feishu" => ChannelType.Feishu,
            "wecom" => ChannelType.WeCom,
            "wechat" => ChannelType.WeChat,
            _ => ChannelType.Feishu
        };

    public static string SerializeChannelType(ChannelType type) =>
        type switch
        {
            ChannelType.Web => "web",
            ChannelType.Feishu => "feishu",
            ChannelType.WeCom => "wecom",
            ChannelType.WeChat => "wechat",
            _ => "feishu"
        };

    public static string MaskSettingsSecrets(string? settingsJson, ChannelType type)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return "{}";

        if (type == ChannelType.Feishu)
        {
            FeishuChannelSettings? settings = DeserializeFeishuSettings(settingsJson);
            if (settings is null) return "{}";

            FeishuChannelSettings masked = settings with
            {
                AppSecret = MaskSecret(settings.AppSecret)
            };
            return JsonSerializer.Serialize(masked);
        }

        return settingsJson;
    }

    internal static string MaskSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return string.Empty;
        if (secret.Length <= 8) return "***";
        return secret[..4] + "***" + secret[^4..];
    }

    private static string? ResolveEnvVars(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Regex.Replace(value, @"\$\{([^}]+)\}", m =>
            Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }
}
