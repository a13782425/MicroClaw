using MicroClaw.Common;
using MicroClaw.Configuration;
using MicroClaw.Core;
using MicroClaw.Core.Logging;
using MicroClaw.Providers.Anthropic;

using MicroClaw.Providers.OpenAI;
using MicroClaw.Utils;

namespace MicroClaw.Providers;
/// <summary>
/// 模型提供方服务：模块对外的唯一入口（<see cref="MicroService"/>），按 <c>providers.yaml</c>
/// 在启动阶段一次性创建 <see cref="ModelProviderObject"/>，停止时全部释放。
/// <para>
/// 所有变更（Upsert / Delete / SetDefault）走 YAML 落盘 + 实例替换：旧实例 <see cref="MicroObject.DisposeAsync"/>，
/// 新实例基于新 <see cref="ProviderEntityConfig"/> 重建底层 SDK 客户端，确保配置与运行时不会脱节。
/// </para>
/// </summary>
public sealed class ModelProviderService : MicroService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ModelProviderObject> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>启动顺序：晚于 IUsageTracker 等基础服务，但早于使用 Provider 的业务服务。</summary>
    public override int Order => 15;

    /// <inheritdoc />
    protected async override ValueTask OnStartAsync(CancellationToken cancellationToken = default)
    {
        //_usageTracker ??= Engine!.GetRequiredService<IUsageTracker>();

        ProvidersOptions options = MicroClawConfig.Get<ProvidersOptions>();


        foreach (ProviderEntityConfig cfg in options.Items)
        {
            if (string.IsNullOrWhiteSpace(cfg.Id))
                continue;

            try
            {
                ModelProviderObject providerObject = CreateProvider(cfg);
                await MicroEngine.Instance.RegisterAsync(providerObject, cancellationToken);
                _providers[cfg.Id] = providerObject;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "无法构造 Provider '{ProviderId}'：{Message}", cfg.Id, ex.Message);
            }
        }

        Logger.LogInformation("ModelProviderService started with {Count} provider(s).", _providers.Count);
    }

    /// <inheritdoc />
    protected override async ValueTask OnDestroyAsync(CancellationToken cancellationToken = default)
    {
        ModelProviderObject[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _providers.Values];
            _providers.Clear();
        }

        foreach (ModelProviderObject provider in snapshot)
        {
            try
            {
                await MicroObject.Destroy(provider, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to dispose provider '{ProviderId}'.", provider.Id);
            }
        }
    }

    // ── Public lookup API ────────────────────────────────────────────────

    /// <summary>按 ID 获取 Provider；未找到时返回 null。</summary>
    public ModelProviderObject? Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate)
        {
            return _providers.TryGetValue(id, out ModelProviderObject? p) ? p : null;
        }
    }

    /// <summary>按 ID 获取 Chat Provider；未找到或类型不匹配时返回 null。</summary>
    public ChatModelClient? FindChat(string id) => Find(id) as ChatModelClient;

    /// <summary>按 ID 获取 Embedding Provider；未找到或类型不匹配时返回 null。</summary>
    public EmbeddingModelClient? FindEmbedding(string id) => Find(id) as EmbeddingModelClient;

    /// <summary>取默认 Chat Provider（IsDefault 优先；否则取首个 Chat）。</summary>
    public ChatModelClient? GetDefaultChat()
    {
        lock (_gate)
        {
            ChatModelClient? first = null;
            foreach (ModelProviderObject p in _providers.Values)
            {
                if (p is not ChatModelClient chat) continue;
                first ??= chat;
                if (chat.IsDefault) return chat;
            }
            return first;
        }
    }

    /// <summary>取默认 Embedding Provider。</summary>
    public EmbeddingModelClient? GetDefaultEmbedding()
    {
        lock (_gate)
        {
            EmbeddingModelClient? first = null;
            foreach (ModelProviderObject p in _providers.Values)
            {
                if (p is not EmbeddingModelClient emb) continue;
                first ??= emb;
                if (emb.IsDefault) return emb;
            }
            return first;
        }
    }

    /// <summary>枚举全部 Chat Provider 的快照。</summary>
    public IReadOnlyList<ChatModelClient> ListChat()
    {
        lock (_gate)
        {
            return [.. _providers.Values.OfType<ChatModelClient>()];
        }
    }

    /// <summary>枚举全部 Embedding Provider 的快照。</summary>
    public IReadOnlyList<EmbeddingModelClient> ListEmbedding()
    {
        lock (_gate)
        {
            return [.. _providers.Values.OfType<EmbeddingModelClient>()];
        }
    }

    /// <summary>枚举全部 Provider 实例的快照。</summary>
    public IReadOnlyList<ModelProviderObject> ListAll()
    {
        lock (_gate)
        {
            return [.. _providers.Values];
        }
    }

    // ── Mutation API ────────────────────────────────────────────────────

    /// <summary>新增或覆盖一条配置；写回 <c>providers.yaml</c> 并替换运行时实例。</summary>
    public void Upsert(ProviderEntityConfig cfg, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        if (string.IsNullOrWhiteSpace(cfg.Id))
            cfg.Id = MicroClawUtils.GetUniqueId();

        ProvidersOptions options = MicroClawConfig.Get<ProvidersOptions>();
        int idx = options.Items.FindIndex(x => string.Equals(x.Id, cfg.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            // 保留旧 ApiKey：当前传入为空时
            if (string.IsNullOrWhiteSpace(cfg.ApiKey))
                cfg.ApiKey = options.Items[idx].ApiKey;
            options.Items[idx] = cfg;
        }
        else
        {
            // 首条设为默认
            if (options.Items.Count == 0)
                cfg.IsDefault = true;
            options.Items.Add(cfg);
            ModelProviderObject providerObject = CreateProvider(cfg);
            lock (_gate)
            {
                _providers[providerObject.Id] = providerObject;
            }
        }

        MicroClawConfig.Save(options);
    }

    /// <summary>删除一条配置；写回 <c>providers.yaml</c> 并释放运行时实例。</summary>
    public async ValueTask DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        ProvidersOptions options = MicroClawConfig.Get<ProvidersOptions>();
        options.Items.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        MicroClawConfig.Save(options);

        ModelProviderObject? removed;
        lock (_gate)
        {
            _providers.Remove(id, out removed);
        }
        if (removed is not null)
        {
            try
            {
                await MicroObject.Destroy(removed, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Dispose provider '{Id}' failed during delete.", id);
            }
        }
    }

    /// <summary>将指定 ID 设为同 ModelKind 下的默认；其它同 Kind 配置的默认标记会被清除。</summary>
    public async ValueTask SetDefaultAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        ProvidersOptions options = MicroClawConfig.Get<ProvidersOptions>();
        ProviderEntityConfig? target = options.Items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (target is null) return;

        foreach (ProviderEntityConfig cfg in options.Items)
        {
            if (string.Equals(cfg.ModelKind, target.ModelKind, StringComparison.OrdinalIgnoreCase))
                cfg.IsDefault = string.Equals(cfg.Id, id, StringComparison.OrdinalIgnoreCase);
        }
        MicroClawConfig.Save(options);
    }

    // ── Internals ───────────────────────────────────────────────────────

    private static ModelProviderObject CreateProvider(ProviderEntityConfig cfg)
    {
        ModelProviderApiKind apiKind = ProviderUtils.ParseApiKind(cfg.ApiKind);
        ModelKind kind = ProviderUtils.ParseModelKind(cfg.ModelKind);

        // 目前仅支持 Chat 类 Provider；其余 ModelKind 暂不支持。
        if (kind != ModelKind.Chat)
            throw new NotSupportedException($"Unsupported provider ModelKind: {kind}");

        return apiKind switch
        {
            // OpenAI Chat Completions 及兼容厂商。
            ModelProviderApiKind.OpenChat => new OpenAIChatModelClient(cfg),
            // OpenAI Responses API（专用 Chat 客户端）。
            ModelProviderApiKind.OpenResponses => new OpenAIResponsesChatModelClient(cfg),
            ModelProviderApiKind.Anthropic => new AnthropicChatModelClient(cfg),
            _ => throw new NotSupportedException($"Unsupported provider ApiKind: {apiKind}"),
        };
    }
}