using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShadUI;

namespace MicroClaw.Desktop;
public partial class MainWindowViewModel : ObservableObject
{
    public const string RouteSessionPrefix = "/sessions/";
    public const string RouteSessionDefault = "/sessions/default";
    public const string RouteMicroAgents = "/micro/agents";
    public const string RouteMicroSkills = "/micro/skills";
    public const string RouteMicroMcp = "/micro/mcp";
    public const string RouteMicroTools = "/micro/tools";
    public const string RouteMicroPlugins = "/micro/plugins";
    public const string RouteSettingsProviders = "/settings/providers";
    public const string RouteSettingsUsage = "/settings/usage";
    public const string RouteSettingsAbout = "/settings/about";
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveThemeLabel))]
    [NotifyPropertyChangedFor(nameof(ThemeIcon))]
    [NotifyPropertyChangedFor(nameof(ThemeButtonToolTip))]
    private ThemeOption? selectedTheme;
    
    [ObservableProperty]
    private bool isSidebarOpen = true;
    
    [ObservableProperty]
    private string currentRoute = RouteSessionDefault;
    
    [ObservableProperty]
    private ObservableObject? currentPage;
    
    private readonly SessionViewModel _defaultSession = SessionViewModel.CreateDefault();
    private readonly MicroAgentsViewModel _microAgents = new();
    private readonly MicroSkillsViewModel _microSkills = new();
    private readonly MicroMcpViewModel _microMcp = new();
    private readonly MicroToolsViewModel _microTools = new();
    private readonly MicroPluginsViewModel _microPlugins = new();
    private readonly ProvidersViewModel _providers = new();
    private readonly AboutViewModel _about = new();
    private readonly UsageViewModel _usage = new();
    
    public MainWindowViewModel()
    {
        SelectedTheme = ThemeOptions[0];
        Navigate(RouteSessionDefault);
    }
    
    public IReadOnlyList<SessionNavItem> Sessions { get; } =
    [
        new(SessionViewModel.DefaultSessionId, "默认会话", "静态模板预览", RouteSessionDefault),
    ];

    public IReadOnlyList<MicroNavItem> MicroItems { get; } =
    [
        new("全局 Agent", Icons.Logo, RouteMicroAgents),
        new("全局 Skill", Icons.Marker, RouteMicroSkills),
        new("全局 MCP", Icons.Search, RouteMicroMcp),
        new("全局 Tools", Icons.Swatch, RouteMicroTools),
        new("全局插件", Icons.Settings, RouteMicroPlugins),
    ];
    
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new("跟随系统", "使用操作系统主题", ThemeMode.System),
        new("浅色", "明亮工作台预览", ThemeMode.Light),
        new("深色", "低亮度工作台预览", ThemeMode.Dark)
    ];
    
    public string ActiveThemeLabel => SelectedTheme?.DisplayName ?? "跟随系统";
    
    public HeroIconsAvalonia.Enums.IconType ThemeIcon
    {
        get
        {
            switch (SelectedTheme?.Mode)
            {
                case ThemeMode.Light:
                    return HeroIconsAvalonia.Enums.IconType.Sun;
                case ThemeMode.Dark:
                    return HeroIconsAvalonia.Enums.IconType.Moon;
                default:
                    return HeroIconsAvalonia.Enums.IconType.ComputerDesktop;
            }
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
    [RelayCommand]
    private void Navigate(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return;
        }

        CurrentRoute = route;

        if (route.StartsWith(RouteSessionPrefix, StringComparison.Ordinal))
        {
            CurrentPage = CreateSessionPage(route[RouteSessionPrefix.Length..]);
            return;
        }

        CurrentPage = route switch
        {
            RouteMicroAgents => _microAgents,
            RouteMicroSkills => _microSkills,
            RouteMicroMcp => _microMcp,
            RouteMicroTools => _microTools,
            RouteMicroPlugins => _microPlugins,
            RouteSettingsProviders => _providers,
            RouteSettingsUsage => _usage,
            RouteSettingsAbout => _about,
            _ => _defaultSession,
        };
    }

    private SessionViewModel CreateSessionPage(string sessionId)
    {
        return sessionId == SessionViewModel.DefaultSessionId
            ? _defaultSession
            : SessionViewModel.CreatePreview(sessionId);
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
        
        App.ThemeWatcher.SwitchTheme(value.Mode);
    }
    
}
public sealed record SessionNavItem(string SessionId, string Title, string Meta, string Route);
public sealed record ThemeOption(string DisplayName, string Description, ThemeMode Mode)
{
    public override string ToString() => DisplayName;
}
public sealed record MicroNavItem(string Title, Geometry? Icon, string Route);