using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicroClaw.Configuration;
using MicroClaw.Desktop.Config;
using MicroClaw.Desktop.Modules;
using MicroClaw.Runtime;
using ShadUI;

namespace MicroClaw.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{

    private readonly MicroViewRouteModule _router = MicroRuntime.Engine.GetRequiredService<MicroViewRouteModule>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveThemeLabel))]
    [NotifyPropertyChangedFor(nameof(ThemeIcon))]
    [NotifyPropertyChangedFor(nameof(ThemeButtonToolTip))]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    [ObservableProperty]
    private string _currentRoute = PageRouteDefine.RouteMicroSession;

    [ObservableProperty]
    private RouteViewModelBase? _currentPage;

    public MainWindowViewModel()
    {
        var settings = MicroClawConfig.Get<MicroClawOptions>().Desktop;
        var savedMode = settings.ThemeMode ?? "跟随系统";
        _selectedTheme = ThemeOptions.FirstOrDefault(t => t.Mode.ToString() == savedMode) ?? ThemeOptions[0];
        _isSidebarOpen = settings.SidebarExpanded;
        Navigate(PageRouteDefine.RouteMicroSession);
    }

    public IReadOnlyList<SessionNavItem> Sessions { get; } =
    [
        new(SessionViewModel.DefaultSessionId, "默认会话", "静态模板预览", PageRouteDefine.RouteMicroSession),
    ];

    public IReadOnlyList<MicroNavItem> MicroItems { get; } =
    [
        new("全局 Agent", Icons.Logo, PageRouteDefine.RouteMicroAgents),
        new("全局 Skill", Icons.Marker, PageRouteDefine.RouteMicroSkills),
        new("全局 MCP", Icons.Search, PageRouteDefine.RouteMicroMcp),
        new("全局 Tools", Icons.Swatch, PageRouteDefine.RouteMicroTools),
        new("全局插件", Icons.Settings, PageRouteDefine.RouteMicroPlugins),
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
        object? routeData = null;
        if (route.StartsWith(PageRouteDefine.RouteMicroSession, StringComparison.Ordinal))
        {
            var sessionId = route[PageRouteDefine.RouteMicroSession.Length..];
            routeData = sessionId;
        }
        CurrentPage = _router.Navigate(route, routeData);

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

    partial void OnIsSidebarOpenChanged(bool value)
    {
        try
        {
            MicroClawOptions microClawOptions = MicroClawConfig.Get<MicroClawOptions>();
            microClawOptions.Desktop.SidebarExpanded = value;
            MicroClawConfig.Save(microClawOptions);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "保存主题设置失败");
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null || Application.Current is not { } app)
        {
            return;
        }

        App.ThemeWatcher.SwitchTheme(value.Mode);
        // 持久化主题选择
        try
        {
            MicroClawOptions microClawOptions = MicroClawConfig.Get<MicroClawOptions>();
            microClawOptions.Desktop.ThemeMode = value.Mode.ToString();
            MicroClawConfig.Save(microClawOptions);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "保存主题设置失败");
        }
    }

}
public sealed record SessionNavItem(string SessionId, string Title, string Meta, string Route);
public sealed record ThemeOption(string DisplayName, string Description, ThemeMode Mode)
{
    public override string ToString() => DisplayName;
}
public sealed record MicroNavItem(string Title, Geometry? Icon, string Route);