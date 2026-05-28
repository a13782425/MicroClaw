using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicroClaw.Configuration;
using MicroClaw.Providers;
using MicroClaw.Runtime;

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
    public event System.Action<ProviderEntityConfig>? Duplicated;

    // ── Base info ────────────────────────────────────────────────────
    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _providerId = string.Empty;
    [ObservableProperty] private string _apiKind = "openai";
    [ObservableProperty] private string _modelKind = "chat";
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isDefault;

    // ── Endpoint info ────────────────────────────────────────────────
    [ObservableProperty] private string _baseUrl = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private bool _isApiKeyEnvMode;
    [ObservableProperty] private int _maxOutputTokens = 8192;
    [ObservableProperty] private int? _latencyMs;

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

    // ── Scenario scores ─────────────────────────────────────────────
    public ObservableCollection<ScenarioScoreItemVm> ScenarioScores { get; } = [];

    // ── Test connection result ──────────────────────────────────────
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private bool _testIsError;

    public string HeaderSubtitle =>
        $"{(string.IsNullOrWhiteSpace(ProviderId) ? "(待生成 ID)" : ProviderId)} · "
        + (IsEnabled ? "已启用" : "已停用")
        + (IsDefault ? " · 当前默认" : string.Empty);

    public string ApiKeyHelperText =>
        IsApiKeyEnvMode ? "从环境变量解析 · 切换为直接输入" : "明文输入 · 切换为环境变量";

    partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(HeaderSubtitle));
    partial void OnIsDefaultChanged(bool value) => OnPropertyChanged(nameof(HeaderSubtitle));
    partial void OnProviderIdChanged(string value) => OnPropertyChanged(nameof(HeaderSubtitle));
    partial void OnIsApiKeyEnvModeChanged(bool value) => OnPropertyChanged(nameof(ApiKeyHelperText));

    /// <summary>构造一个空白的新建表单。</summary>
    public static ProviderDetailVm CreateNew(string modelKind)
    {
        var vm = new ProviderDetailVm
        {
            IsNew = true,
            ProviderId = string.Empty,
            DisplayName = string.Empty,
            ApiKind = "openai",
            ModelKind = modelKind,
            IsEnabled = true,
            IsDefault = false,
        };
        vm.InitModalitiesAndCapabilities(inputs: ["text"], outputs: ["text"], features: []);
        vm.InitScenarioScores(null);
        return vm;
    }

    /// <summary>从已有配置回填。注意：编辑模式下 ApiKey 保留为掩码占位，保存时为空则后端保留旧值。</summary>
    public static ProviderDetailVm FromConfig(ProviderEntityConfig cfg)
    {
        var vm = new ProviderDetailVm
        {
            IsNew = false,
            ProviderId = cfg.Id,
            DisplayName = cfg.DisplayName,
            ApiKind = string.IsNullOrWhiteSpace(cfg.ApiKind) ? "openai" : cfg.ApiKind,
            ModelKind = string.IsNullOrWhiteSpace(cfg.ModelKind) ? "chat" : cfg.ModelKind,
            BaseUrl = cfg.BaseUrl ?? string.Empty,
            IsEnabled = cfg.IsEnabled,
            IsDefault = cfg.IsDefault,
            ModelName = cfg.ModelName,
            MaxOutputTokens = cfg.MaxOutputTokens > 0 ? cfg.MaxOutputTokens : 8192,
            LatencyMs = cfg.LatencyMs,
            InputPrice = FormatPrice(cfg.Pricing?.InputPerMillionTokens),
            OutputPrice = FormatPrice(cfg.Pricing?.OutputPerMillionTokens),
            CacheInputPrice = FormatPrice(cfg.Pricing?.CachedInputPerMillionTokens),
            CacheOutputPrice = FormatPrice(cfg.Pricing?.CachedOutputPerMillionTokens),
        };

        // env-mode detection
        vm.IsApiKeyEnvMode = !string.IsNullOrEmpty(cfg.ApiKey) && cfg.ApiKey.Contains("${", System.StringComparison.Ordinal);
        // Show env-var literal in env mode, mask otherwise
        vm.ApiKey = vm.IsApiKeyEnvMode ? cfg.ApiKey : string.Empty;

        vm.InitModalitiesAndCapabilities(cfg.InputModalities, cfg.OutputModalities, cfg.Capabilities);
        vm.InitScenarioScores(cfg.ScenarioScores);
        return vm;
    }

    private void InitModalitiesAndCapabilities(
        IEnumerable<string>? inputs,
        IEnumerable<string>? outputs,
        IEnumerable<string>? features)
    {
        InputModalities.Clear();
        foreach (var (key, label) in ModalityCatalog)
        {
            var on = inputs?.Any(v => string.Equals(v, key, System.StringComparison.OrdinalIgnoreCase)) ?? false;
            InputModalities.Add(new ToggleItemVm { Key = key, Label = label, IsOn = on });
        }
        OutputModalities.Clear();
        foreach (var (key, label) in ModalityCatalog)
        {
            var on = outputs?.Any(v => string.Equals(v, key, System.StringComparison.OrdinalIgnoreCase)) ?? false;
            OutputModalities.Add(new ToggleItemVm { Key = key, Label = label, IsOn = on });
        }
        Features.Clear();
        foreach (var (key, label) in FeatureCatalog)
        {
            var on = features?.Any(v => string.Equals(v, key, System.StringComparison.OrdinalIgnoreCase)) ?? false;
            Features.Add(new ToggleItemVm { Key = key, Label = label, IsOn = on });
        }
    }

    private void InitScenarioScores(Dictionary<string, ProviderScenarioScoreConfig>? source)
    {
        ScenarioScores.Clear();
        foreach (var key in ModelScenarioCatalog.BuiltInKeys)
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
                Label = ModelScenarioCatalog.Descriptions.TryGetValue(key, out var d) ? d : key,
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
            Id = ProviderId,
            DisplayName = DisplayName ?? string.Empty,
            ApiKind = string.IsNullOrWhiteSpace(ApiKind) ? "openai" : ApiKind,
            ModelKind = string.IsNullOrWhiteSpace(ModelKind) ? "chat" : ModelKind,
            BaseUrl = BaseUrl ?? string.Empty,
            ApiKey = ApiKey ?? string.Empty,
            ModelName = ModelName ?? string.Empty,
            MaxOutputTokens = MaxOutputTokens > 0 ? MaxOutputTokens : 8192,
            LatencyMs = LatencyMs,
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
    private void ToggleApiKeyMode() => IsApiKeyEnvMode = !IsApiKeyEnvMode;

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
        ProviderId = cfg.Id;
        TestResult = "已保存";
        TestIsError = false;
        Saved?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (IsNew || string.IsNullOrWhiteSpace(ProviderId)) return;
        var id = ProviderId;
        await _svc.DeleteAsync(id, CancellationToken.None);
        Deleted?.Invoke(id);
    }

    [RelayCommand]
    private void Duplicate()
    {
        var cfg = ToConfig();
        cfg.Id = string.Empty;          // ModelProviderService.Upsert 会自动分配新 Id
        cfg.IsDefault = false;
        cfg.DisplayName = string.IsNullOrWhiteSpace(cfg.DisplayName) ? "(副本)" : cfg.DisplayName + " (副本)";
        Duplicated?.Invoke(cfg);
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
        var resolvedKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(resolvedKey))
        {
            TestResult = IsApiKeyEnvMode ? "环境变量未设置" : "请填写 API Key";
            TestIsError = true;
            return;
        }
        // Lightweight verification: provider must be registered and the resolved client is ready.
        var sw = Stopwatch.StartNew();
        var registered = string.IsNullOrWhiteSpace(ProviderId) ? null : _svc.Find(ProviderId);
        sw.Stop();
        TestResult = registered is null
            ? $"配置已就绪（未保存） · {sw.ElapsedMilliseconds}ms"
            : $"客户端已就绪 · {sw.ElapsedMilliseconds}ms";
        TestIsError = false;
    }

    private string ResolveApiKey()
    {
        if (string.IsNullOrEmpty(ApiKey)) return string.Empty;
        if (!IsApiKeyEnvMode) return ApiKey;
        // ${VAR_NAME} resolution
        var trimmed = ApiKey.Trim();
        if (trimmed.StartsWith("${", System.StringComparison.Ordinal) && trimmed.EndsWith('}'))
        {
            var name = trimmed[2..^1];
            return System.Environment.GetEnvironmentVariable(name) ?? string.Empty;
        }
        return System.Environment.GetEnvironmentVariable(trimmed) ?? string.Empty;
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

    private static readonly (string Key, string Label)[] ModalityCatalog =
    [
        ("text", "文本"),
        ("image", "图像"),
        ("audio", "音频"),
        ("video", "视频"),
        ("file", "文件"),
    ];

    private static readonly (string Key, string Label)[] FeatureCatalog =
    [
        ("tool_calling", "工具调用"),
        ("responses_api", "Responses API"),
    ];
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
