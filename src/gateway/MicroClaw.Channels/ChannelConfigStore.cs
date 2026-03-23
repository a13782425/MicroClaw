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

    /// <summary>
    /// 幂等创建内置 Web Channel。系统启动时调用，若已存在则跳过。
    /// </summary>
    public void EnsureWebChannel()
    {
        using GatewayDbContext db = factory.CreateDbContext();
        if (db.Channels.Any(e => e.Id == WebChannelId))
            return;

        db.Channels.Add(new ChannelConfigEntity
        {
            Id          = WebChannelId,
            DisplayName = "Web Console",
            ChannelType = "web",
            ProviderId  = string.Empty,
            IsEnabled   = true,
            SettingsJson = "{}"
        });
        db.SaveChanges();
    }

    /// <summary>内置 Web Channel 的固定 ID。</summary>
    public const string WebChannelId = "web";

    private static string MergeSettings(string? existingJson, string incomingJson, ChannelType type)
    {
        if (type != ChannelType.Feishu) return incomingJson;

        FeishuChannelSettings? existing = DeserializeFeishuSettings(existingJson);
        FeishuChannelSettings? incoming = DeserializeFeishuSettings(incomingJson);
        if (existing is null || incoming is null) return incomingJson;

        // 如果字段是掩码值，保留原始值
        string appSecret          = IsMasked(incoming.AppSecret)          ? existing.AppSecret          : incoming.AppSecret;
        string encryptKey         = IsMasked(incoming.EncryptKey)         ? existing.EncryptKey         : incoming.EncryptKey;
        string verificationToken  = IsMasked(incoming.VerificationToken)  ? existing.VerificationToken  : incoming.VerificationToken;

        FeishuChannelSettings merged = incoming with
        {
            AppSecret = appSecret,
            EncryptKey = encryptKey,
            VerificationToken = verificationToken
        };
        return JsonSerializer.Serialize(merged);
    }

    /// <summary>判断字段值是否为掩码占位符（为空或包含 *** 标记）。</summary>
    private static bool IsMasked(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("***");

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
            _ => ChannelType.Web
        };

    public static string SerializeChannelType(ChannelType type) =>
        type switch
        {
            ChannelType.Web => "web",
            ChannelType.Feishu => "feishu",
            ChannelType.WeCom => "wecom",
            ChannelType.WeChat => "wechat",
            _ => "web"
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
                AppSecret = MaskSecret(settings.AppSecret),
                EncryptKey = MaskSecret(settings.EncryptKey),
                VerificationToken = MaskSecret(settings.VerificationToken)
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
