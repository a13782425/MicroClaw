using MicroClaw.Core;
using MicroClaw.Desktop.Base;
namespace MicroClaw.Desktop.Module;
public sealed class MicroViewRouteModule : MicroService
{
    private readonly Dictionary<string, RouteViewModelBase> _registeCache = new();
    private readonly Dictionary<string, Func<RouteViewModelBase>> _registeTypeCache = new();
    protected override ValueTask OnInitializedAsync(CancellationToken cancellationToken = default)
    {
        
        return base.OnInitializedAsync(cancellationToken);
    }
    
    private void Register<T>() where T : RouteViewModelBase, new()
    {
        
    }
    public RouteViewModelBase? Navigate(string? route)
    {
        if (route is null || !_registeTypeCache.TryGetValue(route, out var factory))
            return null;
        RouteViewModelBase? pageVm = default;
        if (!_registeCache.TryGetValue(route, out pageVm))
        {
            pageVm = factory();
            _registeCache.Add(route, pageVm);
        }
        return pageVm;
    }
}