using System.CommandLine;
using System.Net.Http.Headers;

namespace MicroClaw.Commands;

public class ProviderCommand : Command
{
	public ProviderCommand() : base("provider", "管理模型提供方")
	{
		Subcommands.Add(new ProviderListCommand());
	}
}

public class ProviderListCommand : Command
{
	public ProviderListCommand() : base("list", "列出所有已注册的模型提供方")
	{
		var urlOption = new Option<string>("--url")
		{
			Description = "目标服务地址",
			DefaultValueFactory = _ => "http://localhost:5080"
		};
		var tokenOption = new Option<string?>("--token")
		{
			Description = "JWT 认证令牌"
		};
		Options.Add(urlOption);
		Options.Add(tokenOption);

		SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var url = parseResult.GetValue(urlOption)!;
			var token = parseResult.GetValue(tokenOption);
			return await RunAsync(url, token, ct);
		});
	}

	private static async Task<int> RunAsync(string url, string? token, CancellationToken ct)
	{
		Console.WriteLine($"正在获取 {url} 的模型提供方列表...");

		try
		{
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			if (!string.IsNullOrWhiteSpace(token))
				http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

			var response = await http.GetAsync($"{url.TrimEnd('/')}/api/providers", ct);
			var body = await response.Content.ReadAsStringAsync(ct);

			Console.WriteLine($"状态 : {(int)response.StatusCode} {response.StatusCode}");
			Console.WriteLine($"提供方:\n{body}");

			return response.IsSuccessStatusCode ? 0 : 1;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"错误 : {ex.Message}");
			return 1;
		}
	}
}
