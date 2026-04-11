using MicroClaw.Abstractions.Sessions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Abstractions.Pet;

/// <summary>
/// 标记接口：表示一个与 Session 关联的宠物实体。
/// <para>
/// 定义在 <c>MicroClaw.Abstractions</c> 层，使 <see cref="Sessions.Session"/> 可持有宠物引用
/// 而不产生循环依赖。实现类为 <c>MicroClaw.Pet.PetContext</c>。
/// </para>
/// </summary>
public interface IPet
{
    /// <summary>当前 Pet 绑定的会话。</summary>
    IMicroSession MicroSession { get; }

    /// <summary>宠物是否处于可参与编排的激活状态。</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Collect channel-specific AI tools via the bound session's channel.
    /// Default implementation returns an empty list.
    /// </summary>
    Task<IReadOnlyList<AIFunction>> CollectChannelToolsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AIFunction>>([]);
}
