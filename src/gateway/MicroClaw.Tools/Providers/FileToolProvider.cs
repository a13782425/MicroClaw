using MicroClaw.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MicroClaw.Tools;

/// <summary>
/// 文件操作工具提供者，包装 <see cref="FileTools"/>。提供 read/write/edit/list/search 五个工具。
/// 每个会话的文件操作限制在 {sessionsDir}/{sessionId}/sandbox/ 目录内。
/// </summary>
public sealed class FileToolProvider(string sessionsDir, IOptions<FileToolsOptions> options) : IBuiltinToolProvider
{
    public string GroupId => "filesystem";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        FileTools.GetToolDescriptions();

    public IReadOnlyList<AIFunction> CreateTools(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return [];

        string sandboxDir = Path.Combine(sessionsDir, sessionId, "sandbox");
        Directory.CreateDirectory(sandboxDir);
        return FileTools.Create(sandboxDir, options.Value);
    }
}
