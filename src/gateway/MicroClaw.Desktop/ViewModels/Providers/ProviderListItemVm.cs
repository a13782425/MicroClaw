using CommunityToolkit.Mvvm.ComponentModel;
using MicroClaw.Configuration;

namespace MicroClaw.Desktop.ViewModels;

/// <summary>
/// Lightweight row view-model for the providers left-side list.
/// Maps a <see cref="ProviderEntityConfig"/> snapshot into display fields.
/// </summary>
public sealed partial class ProviderListItemVm : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _modelName = string.Empty;
    [ObservableProperty] private string _apiKindLabel = string.Empty;
    [ObservableProperty] private string _modelKind = "chat";
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private bool _hasConfigIssue;
    [ObservableProperty] private string _statusLabel = string.Empty;
    [ObservableProperty] private string _statusColor = "#0F172A";

    public static ProviderListItemVm FromConfig(ProviderEntityConfig cfg)
    {
        var item = new ProviderListItemVm
        {
            Id = cfg.Id,
            DisplayName = string.IsNullOrWhiteSpace(cfg.DisplayName) ? cfg.Id : cfg.DisplayName,
            ModelName = cfg.ModelName,
            ApiKindLabel = MapApiKindLabel(cfg.ApiKind),
            ModelKind = cfg.ModelKind,
            IsEnabled = cfg.IsEnabled,
            IsDefault = cfg.IsDefault,
            HasConfigIssue = string.IsNullOrWhiteSpace(cfg.ApiKey) || string.IsNullOrWhiteSpace(cfg.ModelName),
        };
        item.RefreshStatus();
        return item;
    }

    public void RefreshStatus()
    {
        if (HasConfigIssue)
        {
            StatusLabel = "待补全 API Key";
            StatusColor = "#B45309";
        }
        else if (IsDefault)
        {
            StatusLabel = "默认 · 已启用";
            StatusColor = "#0F172A";
        }
        else if (IsEnabled)
        {
            StatusLabel = "已启用";
            StatusColor = "#0F172A";
        }
        else
        {
            StatusLabel = "已停用";
            StatusColor = "#6B7280";
        }
        OnPropertyChanged(nameof(Subtitle));
    }

    /// <summary>列表行第二段文本："ModelName · ApiKindLabel · StatusLabel"。</summary>
    public string Subtitle
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (!string.IsNullOrWhiteSpace(ModelName)) parts.Add(ModelName);
            if (!string.IsNullOrWhiteSpace(ApiKindLabel)) parts.Add(ApiKindLabel);
            if (!string.IsNullOrWhiteSpace(StatusLabel)) parts.Add(StatusLabel);
            return string.Join(" · ", parts);
        }
    }

    private static string MapApiKindLabel(string? apiKind) =>
        apiKind?.Trim().ToLowerInvariant() switch
        {
            "anthropic" or "claude" => "Anthropic",
            "other" or "openai-compatible" => "OpenAI 兼容",
            _ => "OpenAI",
        };
}
