using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShadUI;

namespace MicroClaw.Desktop;
public partial class MainWindowViewModel : ObservableObject
{
    public const string RouteHome = "/home";
    public const string RouteInbox = "/inbox";
    public const string RouteWorkflows = "/workflows";
    public const string RouteSearch = "/search";
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveThemeLabel))]
    [NotifyPropertyChangedFor(nameof(ThemeIcon))]
    [NotifyPropertyChangedFor(nameof(ThemeButtonToolTip))]
    private ThemeOption? selectedTheme;
    
    [ObservableProperty]
    private bool isSidebarOpen = true;
    
    [ObservableProperty]
    private string currentRoute = RouteHome;
    
    [ObservableProperty]
    private ObservableObject? currentPage;
    
    private readonly HomeViewModel _home = new();
    private readonly InboxViewModel _inbox = new();
    private readonly WorkflowViewModel _workflow = new();
    private readonly SearchViewModel _search = new();
    
    public MainWindowViewModel()
    {
        SelectedTheme = ThemeOptions[0];
        CurrentPage = _home;
    }
    
    public IReadOnlyList<NavItem> NavItems { get; } =
    [
        new("主页", Icons.SidePanel, RouteHome),
        new("收件箱", Icons.Info, RouteInbox),
        new("工作流", Icons.Calendar, RouteWorkflows),
        new("搜索", Icons.Search, RouteSearch),
    ];
    
    [RelayCommand]
    private void Navigate(string? route)
    {
        if (string.IsNullOrEmpty(route))
        {
            return;
        }
        
        CurrentRoute = route;
        CurrentPage = route switch
        {
            RouteHome => _home,
            RouteInbox => _inbox,
            RouteWorkflows => _workflow,
            RouteSearch => _search,
            _ => _home,
        };
    }
    public static IReadOnlyList<Control> SettingsMenuItems { get; } =
    [
        new MenuItem { Header = "首选项", Icon = new PathIcon { Data = Icons.Settings, Width = 14, Height = 14 } },
        new MenuItem { Header = "调色板", Icon = new PathIcon { Data = Icons.Palette, Width = 14, Height = 14 } },
        new Separator(),
        new MenuItem { Header = "关于", Icon = new PathIcon { Data = Icons.Info, Width = 14, Height = 14 } },
    ];
    
    public IReadOnlyList<SessionPreview> Sessions { get; } =
    [
        new("Quick chats", "", false),
        new("microclaw-desktop", "visual-spike", false),
        new("feat: borderless shell", "in-progress", true),
        new("agent-runtime", "preview", false),
        new("workflow-lab", "draft", false),
        new("rag-memory", "notes", false)
    ];
    
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new("跟随系统", "使用操作系统主题", ThemeVariant.Default),
        new("浅色", "明亮工作台预览", ThemeVariant.Light),
        new("深色", "低亮度工作台预览", ThemeVariant.Dark)
    ];
    
    public string ActiveThemeLabel => SelectedTheme?.DisplayName ?? "跟随系统";
    
    public Geometry ThemeIcon
    {
        get
        {
            if (SelectedTheme?.Variant == ThemeVariant.Light)
                return Icons.ArrowDown;
            if (SelectedTheme?.Variant == ThemeVariant.Dark)
                return Icons.Calendar;
            return Icons.ChevronDown;
        }
    }
    public string ThemeButtonToolTip => $"主题：{ActiveThemeLabel}";
    
    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;
    
    [RelayCommand]
    private void CycleTheme()
    {
        var currentIndex = GetSelectedThemeIndex();
        var nextIndex = currentIndex < 0 || currentIndex + 1 >= ThemeOptions.Count ? 0 : currentIndex + 1;
        SelectedTheme = ThemeOptions[nextIndex];
    }
    
    private int GetSelectedThemeIndex()
    {
        for (var index = 0; index < ThemeOptions.Count; index++)
        {
            if (ThemeOptions[index] == SelectedTheme)
            {
                return index;
            }
        }
        
        return -1;
    }
    
    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null || Application.Current is not { } app)
        {
            return;
        }
        
        app.RequestedThemeVariant = value.Variant;
    }
    
}
public sealed record SessionPreview(string Title, string Meta, bool IsActive);
public sealed record ThemeOption(string DisplayName, string Description, ThemeVariant Variant)
{
    public override string ToString() => DisplayName;
}
public sealed record NavItem(string Title, Geometry? Icon, string Route);