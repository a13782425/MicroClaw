using System.Reflection;
using MicroClaw.Core;
using MicroClaw.Desktop.ViewModels;
namespace MicroClaw.Desktop.Modules;
public sealed class MicroViewRouteModule : MicroService
{
    private readonly Dictionary<string, RouteViewModelBase> _pageCache = new();
    private readonly Dictionary<string, Func<RouteViewModelBase>> _factoryCache = new();
    
    protected override ValueTask OnInitializedAsync(CancellationToken cancellationToken = default)
    {
        Register<AboutViewModel>();
        Register<UsageViewModel>();
        Register<ProvidersViewModel>();
        
        Register<MicroAgentsViewModel>();
        Register<MicroSkillsViewModel>();
        Register<MicroMcpViewModel>();
        Register<MicroToolsViewModel>();
        Register<MicroPluginsViewModel>();
        
        Register<SessionViewModel>();
        Register<SessionGameTabViewModel>();
        Register<SessionChatTabViewModel>();
        return base.OnInitializedAsync(cancellationToken);
    }
    
    private void Register<T>() where T : RouteViewModelBase, new()
    {
        var attr = typeof(T).GetCustomAttribute<PageRouteAttribute>();
        if (attr is null) return;
        _factoryCache[attr.Route] = () => new T();
    }
    public RouteViewModelBase? Navigate(string? route, object? parameter = null)
    {
        if (route is null || !_factoryCache.TryGetValue(route, out var factory))
            return null;
        
        if (!_pageCache.TryGetValue(route, out var pageVm))
        {
            pageVm = factory();
            _pageCache.Add(route, pageVm);
        }
        
        pageVm.OnNavigated(parameter);
        return pageVm;
    }
}