using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>
/// HTTP 抓取 AI 工具工厂：生成可被 AI 调用的 fetch_url 函数，用于在对话中读取网页或文档内容。
/// 使用 Microsoft.Extensions.AI 的 AIFunctionFactory（非 MCP），通过 IHttpClientFactory 发出请求。
/// </summary>
public static class FetchTools
{
    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("fetch_url", "Fetch web content"),
    ];

    /// <summary>返回所有内置 Fetch 工具的元数据。供工具列表 API 使用。</summary>
    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>创建 HTTP 抓取工具列表。</summary>
    public static IReadOnlyList<AIFunction> Create(IHttpClientFactory httpClientFactory)
    {
        return
        [
            AIFunctionFactory.Create(
                async (
                    [Description("要抓取的完整 URL，必须以 http:// 或 https:// 开头")] string url,
                    [Description("返回内容的最大字符数（默认 50000），超出部分将被截断，避免占用过多上下文")] int maxLength = 50000) =>
                {
                    // 安全校验：只允许 http/https scheme，拒绝 file://、data: 等
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        return (object)new { success = false, error = $"不支持的 URL scheme，只允许 http:// 和 https://，当前：{url}" };
                    }

                    try
                    {
                        HttpClient client = httpClientFactory.CreateClient("fetch");
                        using HttpResponseMessage response = await client.GetAsync(url);

                        string contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                        string content = await response.Content.ReadAsStringAsync();

                        bool truncated = false;
                        if (content.Length > maxLength)
                        {
                            content = content[..maxLength];
                            truncated = true;
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            return (object)new
                            {
                                success = false,
                                error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                                url,
                                statusCode = (int)response.StatusCode,
                            };
                        }

                        return new
                        {
                            success = true,
                            url,
                            statusCode = (int)response.StatusCode,
                            contentType,
                            content,
                            truncated,
                        };
                    }
                    catch (TaskCanceledException)
                    {
                        return (object)new { success = false, error = "请求超时（30 秒）", url };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { success = false, error = ex.Message, url };
                    }
                },
                name: "fetch_url",
                description: "通过 HTTP GET 请求抓取指定 URL 的文本内容（支持 HTML、Markdown、JSON 等格式）。可用于读取安装文档、API 文档或任何网页内容。"),
        ];
    }
}
