using MicroClaw.Abstractions;
using MicroClaw.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>
/// 文件操作工具提供者，包装 <see cref="FileTools"/>。提供 read/write/edit/list/search 五个工具。
/// 每个会话的文件操作限制在 {sessionsDir}/{sessionId}/sandbox/ 目录内。
/// </summary>
public sealed class FileToolProvider : IToolProvider
{
    private readonly ISandboxUrlGenerator? _urlGenerator;

    public FileToolProvider(IServiceProvider sp)
    {
        _urlGenerator = sp.GetService<ISandboxUrlGenerator>();
    }

    public ToolCategory Category => ToolCategory.Builtin;
    public string GroupId => "filesystem";
    public string DisplayName => "文件操作";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        FileTools.GetToolDescriptions();

    public Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.SessionId))
            return Task.FromResult(ToolProviderResult.Empty);

        string sandboxDir = Path.Combine(MicroClawConfig.Env.SessionsDir, context.SessionId, "sandbox");
        Directory.CreateDirectory(sandboxDir);
        Func<string, string>? gen = _urlGenerator is not null
            ? relPath => _urlGenerator.GenerateDownloadUrl(context.SessionId, relPath)
            : null;
        return Task.FromResult(new ToolProviderResult(FileTools.Create(sandboxDir, MicroClawConfig.Get<FileToolsOptions>(), gen)));
    }
}
