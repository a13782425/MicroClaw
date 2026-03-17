using System.CommandLine;
using System.Net.Http.Headers;

namespace MicroClaw.Commands;

public class GatewayCommand : Command
{
	public GatewayCommand() : base("gateway", "管理网关服务")
	{
		Subcommands.Add(new GatewayRestartCommand());
		Subcommands.Add(new GatewayStopCommand());
	}
}

public class GatewayRestartCommand : Command
{
	public GatewayRestartCommand() : base("restart", "重启网关服务")
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
			return await RunAsync(url, token, "restart", ct);
		});
	}

	internal static async Task<int> RunAsync(string url, string? token, string action, CancellationToken ct)
	{
		Console.WriteLine($"正在向 {url} 发送 {action} 指令...");

		try
		{
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			if (!string.IsNullOrWhiteSpace(token))
				http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

			var response = await http.PostAsync($"{url.TrimEnd('/')}/api/admin/gateway/{action}", null, ct);
			var body = await response.Content.ReadAsStringAsync(ct);

			Console.WriteLine($"状态 : {(int)response.StatusCode} {response.StatusCode}");
			Console.WriteLine($"响应 : {body}");

			return response.IsSuccessStatusCode ? 0 : 1;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"错误 : {ex.Message}");
			return 1;
		}
	}
}

public class GatewayStopCommand : Command
{
	public GatewayStopCommand() : base("stop", "停止网关服务")
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
			return await GatewayRestartCommand.RunAsync(url, token, "stop", ct);
		});
	}
}
