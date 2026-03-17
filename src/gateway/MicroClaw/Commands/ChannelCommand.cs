using System.CommandLine;
using System.Net.Http.Headers;

namespace MicroClaw.Commands;

public class ChannelCommand : Command
{
	public ChannelCommand() : base("channel", "管理接入渠道")
	{
		Subcommands.Add(new ChannelListCommand());
	}
}

public class ChannelListCommand : Command
{
	public ChannelListCommand() : base("list", "列出所有已注册的接入渠道")
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
		Console.WriteLine($"正在获取 {url} 的渠道列表...");

		try
		{
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			if (!string.IsNullOrWhiteSpace(token))
				http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

			var response = await http.GetAsync($"{url.TrimEnd('/')}/api/channels", ct);
			var body = await response.Content.ReadAsStringAsync(ct);

			Console.WriteLine($"状态 : {(int)response.StatusCode} {response.StatusCode}");
			Console.WriteLine($"渠道:\n{body}");

			return response.IsSuccessStatusCode ? 0 : 1;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"错误 : {ex.Message}");
			return 1;
		}
	}
}
