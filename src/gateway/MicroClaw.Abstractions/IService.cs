namespace MicroClaw.Abstractions;

/// <summary>
/// 具有生命周期语义的服务接口。
/// 实现此接口的服务由 <c>ServiceLifetimeHost</c> 统一按 <see cref="InitOrder"/> 顺序初始化和销毁。
/// </summary>
public interface IService : IAsyncDisposable
{
    /// <summary>
    /// 初始化顺序。数值越小越先初始化，越晚销毁。
    /// 推荐值：0 = 基础设施（DB迁移），10 = 数据存储，20 = 核心服务，30 = 外部连接。
    /// </summary>
    int InitOrder { get; }

    /// <summary>异步初始化服务。由 <c>ServiceLifetimeHost</c> 在应用启动时按 <see cref="InitOrder"/> 升序调用。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
