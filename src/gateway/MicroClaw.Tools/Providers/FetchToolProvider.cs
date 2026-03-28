using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>HTTP 抓取工具提供者，包装 <see cref="FetchTools"/>。</summary>
public sealed class FetchToolProvider(IHttpClientFactory httpClientFactory) : IToolProvider
{
    public ToolCategory Category => ToolCategory.Builtin;
    public string GroupId => "fetch";
    public string DisplayName => "HTTP 抓取";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        FetchTools.GetToolDescriptions();

    public Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default) =>
        Task.FromResult(new ToolProviderResult(FetchTools.Create(httpClientFactory)));
}
