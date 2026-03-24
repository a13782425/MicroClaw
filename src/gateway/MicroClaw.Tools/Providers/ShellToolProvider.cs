using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>Shell 命令执行工具提供者，包装 <see cref="ShellTools"/>。</summary>
public sealed class ShellToolProvider : IBuiltinToolProvider
{
    public string GroupId => "shell";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        ShellTools.GetToolDescriptions();

    public IReadOnlyList<AIFunction> CreateTools(string? sessionId) =>
        ShellTools.Create();
}
