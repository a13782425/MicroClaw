namespace MicroClaw.Desktop.ViewModels;

public abstract class RouteViewModelBase: ViewModelBase
{
    // 每次导航到此页时调用（不限首次），parameter 由调用方传入
    protected internal virtual void OnNavigated(object? parameter) { }
}