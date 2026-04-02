using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Abstractions;
using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Channels;

public sealed class ChannelConfigStore(string configDir)
    : YamlFileStore<ChannelConfigEntity>(Path.Combine(configDir, "channels.yaml"), e => e.Id)
{
    public IReadOnlyList<ChannelConfig> All
        => GetAll().Select(ToConfig).ToList().AsReadOnly();

    public ChannelConfig? GetById(string id)
        => GetYamlById(id) is { } e ? ToConfig(e) : null;

    public IReadOnlyList<ChannelConfig> GetByType(ChannelType type)
    {
        string typeStr = SerializeChannelType(type);
        return GetAll().Where(e => e.ChannelType == typeStr).Select(ToConfig).ToList().AsReadOnly();
    }

    public ChannelConfig Add(ChannelConfig config)
    {
        ChannelConfigEntity entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });
        SetYaml(entity);
        return ToConfig(entity);
    }

    public ChannelConfig? Update(string id, ChannelConfig incoming)
    {
        var updated = MutateYaml(id, e =>
        {
            e.DisplayName = incoming.DisplayName;
            e.ChannelType = SerializeChannelType(incoming.ChannelType);
            e.IsEnabled = incoming.IsEnabled;
            e.SettingsJson = MergeSettings(e.SettingsJson, incoming.SettingsJson, incoming.ChannelType);
        });
        return updated is null ? null : ToConfig(updated);
    }

    public bool Delete(string id) => RemoveYaml(id);

    /// <summary>幂等创建内置 Web Channel。系统启动时调用，若已存在则跳过。</summary>
    public void EnsureWebChannel()
    {
        if (ContainsYaml(WebChannelId)) return;
        SetYaml(new ChannelConfigEntity
        {
            Id = WebChannelId,
            DisplayName = "Web Console",
            ChannelType = "web",
            IsEnabled = true,
            SettingsJson = "{}"
        });
    }

    /// <summary>内置 Web Channel 的固定 ID。</summary>
    public const string WebChannelId = "web";

    private static string MergeSettings(string? existingJson, string incomingJson, ChannelType type)
    {
        if (type != ChannelType.Feishu) return incomingJson;

        FeishuChannelSettings? existing = DeserializeFeishuSettings(existingJson);
        FeishuChannelSettings? incoming = DeserializeFeishuSettings(incomingJson);
        if (existing is null || incoming is null) return incomingJson;

        string appSecret         = IsMasked(incoming.AppSecret)         ? existing.AppSecret         : incoming.AppSecret;
        string encryptKey        = IsMasked(incoming.EncryptKey)        ? existing.EncryptKey        : incoming.EncryptKey;
        string verificationToken = IsMasked(incoming.VerificationToken) ? existing.VerificationToken : incoming.VerificationToken;

        FeishuChannelSettings merged = incoming with
        {
            AppSecret = appSecret,
            EncryptKey = encryptKey,
            VerificationToken = verificationToken
        };
        return JsonSerializer.Serialize(merged);
    }

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
            IsEnabled = e.IsEnabled,
            SettingsJson = ResolveEnvVars(e.SettingsJson) ?? "{}"
        };

    private static ChannelConfigEntity ToEntity(ChannelConfig c) =>
        new()
        {
            Id = c.Id,
            DisplayName = c.DisplayName,
            ChannelType = SerializeChannelType(c.ChannelType),
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
