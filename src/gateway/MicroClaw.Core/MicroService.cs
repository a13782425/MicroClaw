namespace MicroClaw.Core;

/// <summary>
/// 引擎服务的抽象基类：无父 obj 的全局子系统，随引擎启停而启停。
/// 经引擎轻量 service locator（<see cref="MicroEngine.GetService{T}"/>）按类型取用——非 DI 容器，
/// 不做构造注入/依赖解析；服务跨调用共享，需自身线程安全。
/// <para>
/// 作为叶子节点（无子节点要编排），子类直接重写基类生命周期钩子：
/// <see cref="MicroLifecycle.OnStartAsync"/>（启动）、<see cref="MicroLifecycle.OnDisableAsync"/>（停止），
/// 按需 <c>OnAwakeAsync</c> / <c>OnEnableAsync</c> / <c>OnDestroyAsync</c>。
/// </para>
/// </summary>
public abstract class MicroService : MicroLifecycle
{
    /// <summary>启动顺序，数值越小越先启动（停止时逆序）。</summary>
    public virtual int Order => 0;

    /// <summary>所属引擎，未注册时为 null。由 <see cref="MicroEngine"/> 在注册/销毁时维护。</summary>
    public MicroEngine? Engine { get; internal set; }

    /// <summary>销毁请求入口：挂在引擎上时交给引擎协调；脱离引擎时直接本地实际拆毁。</summary>
    internal override ValueTask DestroyCoreAsync(CancellationToken cancellationToken = default)
        => Engine is { } engine ? engine.DestroyServiceAsync(this) : DestroyFromEngineAsync(cancellationToken);

    /// <summary>由引擎协调完成的实际拆毁（走 base，不再经 Engine 路由，避免递归）。</summary>
    internal ValueTask DestroyFromEngineAsync(CancellationToken cancellationToken = default)
        => base.DestroyCoreAsync(cancellationToken);
}
