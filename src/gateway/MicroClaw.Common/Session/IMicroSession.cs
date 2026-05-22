namespace MicroClaw.Common;

/// <summary>
/// 会话运行时契约，暴露于模块边界。
/// </summary>
public interface IMicroSession
{
    string Id { get; }
    string Title { get; }
}