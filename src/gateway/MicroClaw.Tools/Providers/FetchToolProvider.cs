using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>HTTP 抓取工具提供者，包装 <see cref="FetchTools"/>。</summary>
public sealed class FetchToolProvider(IHttpClientFactory httpClientFactory) : IBuiltinToolProvider
{
    public string GroupId => "fetch";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        FetchTools.GetToolDescriptions();

    public IReadOnlyList<AIFunction> CreateTools(string? sessionId) =>
        FetchTools.Create(httpClientFactory);
}
