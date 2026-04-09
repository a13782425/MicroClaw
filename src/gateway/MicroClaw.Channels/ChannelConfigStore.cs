using System.Text.Json;
using System.Text.RegularExpressions;
using MicroClaw.Abstractions;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Utils;

namespace MicroClaw.Channels;

/// <summary>
/// 渠道配置存储，作为 <see cref="MicroClawConfig"/> 的线程安全二次封装。
/// 所有写操作通过 <see cref="MicroClawConfig.Save{T}"/> 持久化到 channels.yaml。
/// </summary>
public sealed class ChannelConfigStore : IService
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    // ── IService ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public int InitOrder => 10;

    /// <summary>确保内置 Web Channel 存在（幂等）。</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        EnsureWebChannel();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<ChannelEntity> All
    {
        get
        {
            _lock.EnterReadLock();
            try { return [.. MicroClawConfig.Get<ChannelOptions>().Channels.Select(WithResolvedEnvVars)]; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public ChannelEntity? GetById(string id)
    {
        _lock.EnterReadLock();
        try
        {
            var e = MicroClawConfig.Get<ChannelOptions>().Channels.FirstOrDefault(c => c.Id == id);
            return e is null ? null : WithResolvedEnvVars(e);
        }
        finally { _lock.ExitReadLock(); }
    }

    public IReadOnlyList<ChannelEntity> GetByType(ChannelType type)
    {
        _lock.EnterReadLock();
        try
        {
            return [.. MicroClawConfig.Get<ChannelOptions>().Channels
                .Where(c => c.ChannelType == type)
                .Select(WithResolvedEnvVars)];
        }
        finally { _lock.ExitReadLock(); }
    }

    public ChannelEntity Add(ChannelEntity channel)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ChannelOptions>();
            var withId = new ChannelEntity
            {
                Id          = MicroClawUtils.GetUniqueId(),
                DisplayName = channel.DisplayName,
                ChannelType = channel.ChannelType,
                IsEnabled   = channel.IsEnabled,
                SettingJson = channel.SettingJson,
            };
            MicroClawConfig.Save(new ChannelOptions { Channels = [.. opts.Channels, withId] });
            return withId;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public ChannelEntity? Update(string id, ChannelEntity incoming)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ChannelOptions>();
            var existing = opts.Channels.FirstOrDefault(c => c.Id == id);
            if (existing is null) return null;

            var merged = new ChannelEntity
            {
                Id          = id,
                DisplayName = incoming.DisplayName,
                ChannelType = incoming.ChannelType,
                IsEnabled   = incoming.IsEnabled,
                SettingJson = MergeSettings(existing.SettingJson, incoming.SettingJson, incoming.ChannelType),
            };
            var updatedList = opts.Channels.Select(c => c.Id == id ? merged : c).ToList();
            MicroClawConfig.Save(new ChannelOptions { Channels = updatedList });
            return merged;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public bool Delete(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ChannelOptions>();
            var newList = opts.Channels.Where(c => c.Id != id).ToList();
            if (newList.Count == opts.Channels.Count) return false;
            MicroClawConfig.Save(new ChannelOptions { Channels = newList });
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>幂等创建内置 Web Channel。系统启动时调用，若已存在则跳过。</summary>
    public void EnsureWebChannel()
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<ChannelOptions>();
            if (opts.Channels.Any(c => c.Id == WebChannelId)) return;
            MicroClawConfig.Save(new ChannelOptions
            {
                Channels = [.. opts.Channels, new ChannelEntity
                {
                    Id          = WebChannelId,
                    DisplayName = "Web Console",
                    ChannelType = ChannelType.Web,
                    IsEnabled   = true,
                    SettingJson = "{}",
                }]
            });
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>内置 Web Channel 的固定 ID。</summary>
    public const string WebChannelId = "web";

    private static string MergeSettings(string existingJson, string incomingJson, ChannelType type)
    {
        if (type != ChannelType.Feishu) return incomingJson;

        FeishuChannelSettings? existing = DeserializeFeishuSettings(existingJson);
        FeishuChannelSettings? incoming = DeserializeFeishuSettings(incomingJson);
        if (existing is null || incoming is null) return incomingJson;

        string appSecret         = IsMasked(incoming.AppSecret)         ? existing.AppSecret         : incoming.AppSecret;
        string encryptKey        = IsMasked(incoming.EncryptKey)        ? existing.EncryptKey        : incoming.EncryptKey;
        string verificationToken = IsMasked(incoming.VerificationToken) ? existing.VerificationToken : incoming.VerificationToken;

        return JsonSerializer.Serialize(incoming with
        {
            AppSecret         = appSecret,
            EncryptKey        = encryptKey,
            VerificationToken = verificationToken,
        });
    }

    private static bool IsMasked(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("***");

    private static FeishuChannelSettings? DeserializeFeishuSettings(string? json) =>
        FeishuChannelSettings.TryParse(json);

    public static ChannelType ParseChannelType(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "web"    => ChannelType.Web,
            "feishu" => ChannelType.Feishu,
            "wecom"  => ChannelType.WeCom,
            "wechat" => ChannelType.WeChat,
            _        => ChannelType.Web
        };

    public static string SerializeChannelType(ChannelType type) =>
        type switch
        {
            ChannelType.Web    => "web",
            ChannelType.Feishu => "feishu",
            ChannelType.WeCom  => "wecom",
            ChannelType.WeChat => "wechat",
            _                  => "web"
        };

    public static string MaskSettingsSecrets(string? settingsJson, ChannelType type)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return "{}";

        if (type == ChannelType.Feishu)
        {
            FeishuChannelSettings? settings = DeserializeFeishuSettings(settingsJson);
            if (settings is null) return "{}";

            return JsonSerializer.Serialize(settings with
            {
                AppSecret         = MaskSecret(settings.AppSecret),
                EncryptKey        = MaskSecret(settings.EncryptKey),
                VerificationToken = MaskSecret(settings.VerificationToken),
            });
        }

        return settingsJson;
    }

    internal static string MaskSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return string.Empty;
        if (secret.Length <= 8) return "***";
        return secret[..4] + "***" + secret[^4..];
    }

    private static ChannelEntity WithResolvedEnvVars(ChannelEntity e) =>
        new()
        {
            Id          = e.Id,
            DisplayName = e.DisplayName,
            ChannelType = e.ChannelType,
            IsEnabled   = e.IsEnabled,
            SettingJson = ResolveEnvVars(e.SettingJson) ?? "{}",
        };

    private static string? ResolveEnvVars(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Regex.Replace(value, @"\$\{([^}]+)\}", m =>
            Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }
}
