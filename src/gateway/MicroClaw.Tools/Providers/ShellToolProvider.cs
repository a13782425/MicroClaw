using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>Shell 命令执行工具提供者，包装 <see cref="ShellTools"/>。</summary>
public sealed class ShellToolProvider : IToolProvider
{
    public ToolCategory Category => ToolCategory.Builtin;
    public string GroupId => "shell";
    public string DisplayName => "Shell 命令";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        ShellTools.GetToolDescriptions();

    public Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default) =>
        Task.FromResult(new ToolProviderResult(ShellTools.Create()));
}
