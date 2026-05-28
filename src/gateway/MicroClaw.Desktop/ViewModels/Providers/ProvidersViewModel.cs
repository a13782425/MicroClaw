using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicroClaw.Providers;
using MicroClaw.Runtime;

namespace MicroClaw.Desktop.ViewModels;

/// <summary>
/// Master view-model for the providers page (left list + right detail).
/// </summary>
[PageRoute(PageRouteDefine.RouteSettingsProviders)]
public sealed partial class ProvidersViewModel : RouteViewModelBase
{
    private readonly ModelProviderService _svc = MicroRuntime.Engine.GetRequiredService<ModelProviderService>();

    public ObservableCollection<ProviderListItemVm> ChatItems { get; } = [];
    public ObservableCollection<ProviderListItemVm> EmbeddingItems { get; } = [];

    public ObservableCollection<ProviderListItemVm> FilteredChatItems { get; } = [];
    public ObservableCollection<ProviderListItemVm> FilteredEmbeddingItems { get; } = [];

    public string Title { get; } = "模型提供方";
    public string Description { get; } = "配置聊天与嵌入模型的接入参数、模态能力、价格与场景评分。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatTabActive))]
    [NotifyPropertyChangedFor(nameof(IsEmbeddingTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveItems))]
    [NotifyPropertyChangedFor(nameof(FooterStat))]
    private string _activeTab = "chat";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ProviderListItemVm? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ProviderDetailVm? _detailVm;

    public bool IsChatTabActive => ActiveTab == "chat";
    public bool IsEmbeddingTabActive => ActiveTab == "embedding";
    public bool HasSelection => DetailVm is not null;

    public ObservableCollection<ProviderListItemVm> ActiveItems =>
        IsEmbeddingTabActive ? FilteredEmbeddingItems : FilteredChatItems;

    public string FooterStat
    {
        get
        {
            int total = IsEmbeddingTabActive ? EmbeddingItems.Count : ChatItems.Count;
            int issues = (IsEmbeddingTabActive ? EmbeddingItems : ChatItems).Count(x => x.HasConfigIssue);
            string kindLabel = IsEmbeddingTabActive ? "个嵌入模型" : "个聊天模型";
            return issues > 0 ? $"共 {total} {kindLabel} · {issues} 个待配置" : $"共 {total} {kindLabel}";
        }
    }

    public ProvidersViewModel()
    {
        LoadProviders();
    }

    protected internal override void OnNavigated(object? parameter) => LoadProviders();

    partial void OnSearchTextChanged(string value) => RefreshFiltered();

    partial void OnSelectedItemChanged(ProviderListItemVm? value)
    {
        if (value is null)
        {
            return;
        }
        var cfg = _svc.Find(value.Id)?.Config;
        DetailVm = cfg is not null ? BuildDetail(new ProviderDetailVm(cfg)) : null;
    }

    [RelayCommand]
    private void SetActiveTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab)) return;
        ActiveTab = tab == "embedding" ? "embedding" : "chat";
        SelectedItem = null;
        DetailVm = null;
        RefreshFiltered();
    }

    [RelayCommand]
    private void AddNew()
    {
        SelectedItem = null;
        var vm = new ProviderDetailVm(ActiveTab);
        DetailVm = BuildDetail(vm);
    }
    private ProviderDetailVm BuildDetail(ProviderDetailVm vm)
    {
        vm.Saved += LoadProviders;
        vm.Deleted += id =>
        {
            DetailVm = null;
            SelectedItem = null;
            LoadProviders();
        };
        return vm;
    }

    private void LoadProviders()
    {
        ChatItems.Clear();
        EmbeddingItems.Clear();
        foreach (ModelProviderObject p in _svc.ListAll())
        {
            ProviderListItemVm item = new ProviderListItemVm(p);
            if (string.Equals(item.ModelKind, ModelKind.Embedding.ToString(), System.StringComparison.OrdinalIgnoreCase))
                EmbeddingItems.Add(item);
            else
                ChatItems.Add(item);
        }
        RefreshFiltered();
    }

    private void RefreshFiltered()
    {
        ApplyFilter(ChatItems, FilteredChatItems);
        ApplyFilter(EmbeddingItems, FilteredEmbeddingItems);
        OnPropertyChanged(nameof(ActiveItems));
        OnPropertyChanged(nameof(FooterStat));
    }

    private void ApplyFilter(IEnumerable<ProviderListItemVm> source, ObservableCollection<ProviderListItemVm> target)
    {
        target.Clear();
        var q = (SearchText ?? string.Empty).Trim();
        foreach (var item in source)
        {
            if (q.Length == 0
                || item.DisplayName.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                || item.ModelName.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                || item.Id.Contains(q, System.StringComparison.OrdinalIgnoreCase))
            {
                target.Add(item);
            }
        }
    }
}
