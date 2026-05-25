using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicroClaw.Desktop.Modules;
using MicroClaw.Runtime;

namespace MicroClaw.Desktop.ViewModels;
[PageRoute(PageRouteDefine.RouteMicroSession)]
public partial class SessionViewModel : RouteViewModelBase
{
    public const string DefaultSessionId = "default";
    private readonly MicroViewRouteModule _router = MicroRuntime.Engine.GetRequiredService<MicroViewRouteModule>();
    
    public string? SessionId { get; private set; }
    
    public string? Title { get; private set; }
    
    protected internal override void OnNavigated(object? parameter)
    {
        if (parameter is not string sessionId || sessionId == SessionId)
            return;
        SessionId = sessionId;
        Title = "静态模板预览";
        NavigateTab(PageRouteDefine.RouteSessionChat);
    }
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatTabActive))]
    [NotifyPropertyChangedFor(nameof(IsGameTabActive))]
    private string _currentTabRoute = PageRouteDefine.RouteSessionChat;
    
    [ObservableProperty]
    private ObservableObject? _currentTabPage;
    
    public bool IsChatTabActive => CurrentTabRoute == PageRouteDefine.RouteSessionChat;
    
    public bool IsGameTabActive => CurrentTabRoute == PageRouteDefine.RouteSessionGame;
    
    [RelayCommand]
    private void NavigateTab(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return;
        CurrentTabRoute = route;
        CurrentTabPage = _router.Navigate(route, SessionId);
    }
}