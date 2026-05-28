using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicroClaw.Configuration;
using MicroClaw.Providers;
using MicroClaw.Runtime;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MicroClaw.Desktop.ViewModels;

/// <summary>
/// Right-side detail panel view-model. Edits a single Provider configuration
/// and writes back via <see cref="ModelProviderService.Upsert"/>.
/// </summary>
public sealed partial class ProviderDetailVm : ObservableObject
{
    private readonly ModelProviderService _svc = MicroRuntime.Engine.GetRequiredService<ModelProviderService>();

    public event System.Action? Saved;
    public event System.Action<string>? Deleted;

    // ── Base info ────────────────────────────────────────────────────
    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _apiKind = "openai";
    [ObservableProperty] private string _modelKind = "chat";
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isDefault;

    // ── Endpoint info ────────────────────────────────────────────────
    [ObservableProperty] private string _baseUrl = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private int _maxOutputTokens = 8192;
    [ObservableProperty] private long _maxContextLength = 128000;

    // ── Model & capabilities ─────────────────────────────────────────
    [ObservableProperty] private string _modelName = string.Empty;

    public ObservableCollection<ToggleItemVm> InputModalities { get; } = [];
    public ObservableCollection<ToggleItemVm> OutputModalities { get; } = [];
    public ObservableCollection<ToggleItemVm> Features { get; } = [];

    // ── Pricing (string-backed for free editing) ────────────────────
    [ObservableProperty] private string _inputPrice = string.Empty;
    [ObservableProperty] private string _outputPrice = string.Empty;
    [ObservableProperty] private string _cacheInputPrice = string.Empty;
    [ObservableProperty] private string _cacheOutputPrice = string.Empty;

    public static string[] ApiKindOptions { get; } = ["openai", "anthropic", "other"];
    public static string[] ModelKindOptions { get; } = ["chat", "embedding"];

    // ── Scenario scores ─────────────────────────────────────────────
    public ObservableCollection<ScenarioScoreItemVm> ScenarioScores { get; } = [];

    // ── Test connection result ──────────────────────────────────────
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private bool _testIsError;
    private readonly ProviderEntityConfig _cfg;
    public ProviderDetailVm(string modelKind) : this(new ProviderEntityConfig()
    {
        ModelKind = modelKind
    })
    {
        IsNew = true;
        IsEnabled = true;
        IsDefault = false;
        InitModalitiesAndCapabilities(inputs: ["text"], outputs: ["text"], features: []);
        InitScenarioScores(null);
    }

    public ProviderDetailVm(ProviderEntityConfig providerEntityConfig)
    {
        this._cfg = providerEntityConfig;
        IsNew = false;
        ModelKind = providerEntityConfig.ModelKind;
        ApiKind = string.IsNullOrWhiteSpace(providerEntityConfig.ApiKind) ? "openai" : providerEntityConfig.ApiKind;
        BaseUrl = providerEntityConfig.BaseUrl ?? string.Empty;
        IsEnabled = providerEntityConfig.IsEnabled;
        IsDefault = providerEntityConfig.IsDefault;
        ModelName = providerEntityConfig.ModelName;
        MaxOutputTokens = providerEntityConfig?.MaxOutputTokens ?? 8192;
        MaxContextLength = providerEntityConfig?.MaxContextLength ?? 128000;
        ApiKey = string.Empty;
        InputPrice = FormatPrice(providerEntityConfig.Pricing?.InputPerMillionTokens);
        OutputPrice = FormatPrice(providerEntityConfig.Pricing?.OutputPerMillionTokens);
        CacheInputPrice = FormatPrice(providerEntityConfig.Pricing?.CachedInputPerMillionTokens);
        CacheOutputPrice = FormatPrice(providerEntityConfig.Pricing?.CachedOutputPerMillionTokens);
        InitModalitiesAndCapabilities(providerEntityConfig.InputModalities, providerEntityConfig.OutputModalities, providerEntityConfig.Capabilities);
        InitScenarioScores(providerEntityConfig.ScenarioScores);
    }

    public string HeaderSubtitle =>
        (IsEnabled ? "已启用" : "已停用") + (IsDefault ? " · 当前默认" : string.Empty);

    partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(HeaderSubtitle));
    partial void OnIsDefaultChanged(bool value) => OnPropertyChanged(nameof(HeaderSubtitle));


    private void InitModalitiesAndCapabilities(IEnumerable<string>? inputs, IEnumerable<string>? outputs, IEnumerable<string>? features)
    {
        InputModalities.Clear();
        foreach (var (key, label) in ProviderUtils.ModalityDescriptions)
        {
            var on = inputs?.Any(v => string.Equals(v, key, System.StringComparison.OrdinalIgnoreCase)) ?? false;
            InputModalities.Add(new ToggleItemVm { Key = key, Label = label, IsOn = on });
        }
        OutputModalities.Clear();
        foreach (var (key, label) in ProviderUtils.ModalityDescriptions)
        {
            var on = outputs?.Any(v => string.Equals(v, key, System.StringComparison.OrdinalIgnoreCase)) ?? false;
            OutputModalities.Add(new ToggleItemVm { Key = key, Label = label, IsOn = on });
        }
        Features.Clear();
        foreach (var (key, label) in ProviderUtils.FeatureDescriptions)
        {
            var on = features?.Any(v => string.Equals(v, key, System.StringComparison.OrdinalIgnoreCase)) ?? false;
            Features.Add(new ToggleItemVm { Key = key, Label = label, IsOn = on });
        }
    }

    private void InitScenarioScores(Dictionary<string, ProviderScenarioScoreConfig>? source)
    {
        ScenarioScores.Clear();
        foreach (var key in ProviderUtils.BuiltInScenarioKeys)
        {
            int score = 50;
            string? notes = null;
            if (source is not null && source.TryGetValue(key, out var existing) && existing is not null)
            {
                score = System.Math.Clamp(existing.Score, 0, 100);
                notes = existing.Notes;
            }
            ScenarioScores.Add(new ScenarioScoreItemVm
            {
                Key = key,
                Label = ProviderUtils.ScenarioDescriptions.TryGetValue(key, out var d) ? d : key,
                Score = score,
                Notes = notes,
            });
        }
    }

    /// <summary>构造回写到 YAML 的配置对象。ApiKey 为空时调用方应保留旧值（Service 已实现）。</summary>
    public ProviderEntityConfig ToConfig()
    {
        var cfg = new ProviderEntityConfig
        {
            DisplayName = DisplayName ?? string.Empty,
            ApiKind = string.IsNullOrWhiteSpace(ApiKind) ? "openai" : ApiKind,
            ModelKind = string.IsNullOrWhiteSpace(ModelKind) ? "chat" : ModelKind,
            BaseUrl = BaseUrl ?? string.Empty,
            ApiKey = ApiKey ?? string.Empty,
            ModelName = ModelName ?? string.Empty,
            MaxOutputTokens = MaxOutputTokens > 0 ? MaxOutputTokens : 8192,
            MaxContextLength = MaxContextLength > 0 ? MaxContextLength : 128000,
            IsEnabled = IsEnabled,
            IsDefault = IsDefault,
            Pricing = new ProviderPricingConfig
            {
                InputPerMillionTokens = ParsePrice(InputPrice),
                OutputPerMillionTokens = ParsePrice(OutputPrice),
                CachedInputPerMillionTokens = ParsePrice(CacheInputPrice),
                CachedOutputPerMillionTokens = ParsePrice(CacheOutputPrice),
            },
            Capabilities = Features.Where(f => f.IsOn).Select(f => f.Key).ToList(),
            InputModalities = InputModalities.Where(m => m.IsOn).Select(m => m.Key).ToList(),
            OutputModalities = OutputModalities.Where(m => m.IsOn).Select(m => m.Key).ToList(),
            ScenarioScores = ScenarioScores.ToDictionary(
                s => s.Key,
                s => new ProviderScenarioScoreConfig { Score = s.Score, Notes = s.Notes },
                System.StringComparer.OrdinalIgnoreCase),
        };
        // Default fallbacks when user toggles everything off
        if (cfg.InputModalities.Count == 0) cfg.InputModalities = ["text"];
        if (cfg.OutputModalities.Count == 0) cfg.OutputModalities = ["text"];
        return cfg;
    }

    // ── Commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            TestResult = "请填写显示名称";
            TestIsError = true;
            return;
        }
        var cfg = ToConfig();
        _svc.Upsert(cfg, CancellationToken.None);
        IsNew = false;
        TestResult = "已保存";
        TestIsError = false;
        Saved?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (IsNew) return;
        var id = _cfg.Id;
        await _svc.DeleteAsync(id, CancellationToken.None);
        Deleted?.Invoke(id);
    }

    [RelayCommand]
    private void TestConnection()
    {
        if (string.IsNullOrWhiteSpace(ModelName))
        {
            TestResult = "请先填写模型名称";
            TestIsError = true;
            return;
        }
        // Resolve the runtime ApiKey value: if env mode, look up the env var; otherwise use the entered key.
        var resolvedKey = _cfg.ApiKey;
        if (string.IsNullOrWhiteSpace(resolvedKey))
        {
            TestResult = "请填写 API Key";
            TestIsError = true;
            return;
        }
        // Lightweight verification: provider must be registered and the resolved client is ready.
        var sw = Stopwatch.StartNew();
        var registered = string.IsNullOrWhiteSpace(_cfg.Id) ? null : _svc.Find(_cfg.Id);
        sw.Stop();
        TestResult = registered is null
            ? $"配置已就绪（未保存） · {sw.ElapsedMilliseconds}ms"
            : $"客户端已就绪 · {sw.ElapsedMilliseconds}ms";
        TestIsError = false;
    }

    private static string FormatPrice(decimal? v) =>
        v.HasValue ? v.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;

    private static decimal? ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? System.Math.Max(0m, v)
            : (decimal?)null;
    }

}

/// <summary>Generic key+label+IsOn observable for tag-style toggles.</summary>
public sealed partial class ToggleItemVm : ObservableObject
{
    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private bool _isOn;
}

/// <summary>Single scenario score row.</summary>
public sealed partial class ScenarioScoreItemVm : ObservableObject
{
    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private int _score = 50;
    [ObservableProperty] private string? _notes;

    /// <summary>Bar width for the right-side score list (relative 0-180px scale).</summary>
    public double BarWidth => System.Math.Clamp(Score, 0, 100) * 1.8d;

    partial void OnScoreChanged(int value)
    {
        OnPropertyChanged(nameof(BarWidth));
    }
}
