using MicroClaw.Core;
using MicroClaw.Core.Logging;
namespace MicroClaw.Runtime;
public static class MicroRuntime
{
    private static volatile bool _isStart = false;
    
    private static MicroEngine _engine = null!;
    public static MicroEngine Engine
    {
        get
        {
            if (!_isStart)
                throw new InvalidOperationException("无法在引擎启动前访问 Engine 属性。");
            return _engine;
        }
    }
    
    /// <summary>
    /// 开始运行引擎。调用此方法后将无法访问 Engine 属性。
    /// </summary>
    /// <param name="factory"></param>
    /// <param name="cancellationToken"></param>
    public static async Task StartAsync(IMicroLoggerFactory factory, CancellationToken cancellationToken = default)
    {
        _engine = new MicroEngine(factory);
        await _engine.StartEngine(cancellationToken);
        _isStart = true;
    }
    public static async Task RegisterServiceAsync(CancellationToken cancellationToken = default)
    {
        if (!_isStart)
            throw new InvalidOperationException("无法在引擎启动前注册服务。");
        // await Engine.RegisterServiceAsync(new ModelProviderService(), cancellationToken);
        // await Engine.RegisterServiceAsync(new PetService(), cancellationToken);
        // await Engine.RegisterServiceAsync(new SessionService(), cancellationToken);
    }
    
    public static async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isStart)
            throw new InvalidOperationException("无法在引擎启动前停止引擎。");
        await Engine.StopEngine(cancellationToken);
        _isStart = false;
    }
}