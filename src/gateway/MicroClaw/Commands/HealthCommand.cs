using System.CommandLine;

namespace MicroClaw.Commands;

public class HealthCommand : Command
{
	public HealthCommand() : base("health", "检查服务健康状态")
	{
		var urlOption = new Option<string>("--url")
		{
			Description = "目标服务地址",
			DefaultValueFactory = _ => "http://localhost:5080"
		};
		Options.Add(urlOption);

		SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var url = parseResult.GetValue(urlOption)!;
			return await RunAsync(url, ct);
		});
	}

	private static async Task<int> RunAsync(string url, CancellationToken ct)
	{
		Console.WriteLine($"Checking {url}/api/health ...");

		try
		{
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
			var response = await http.GetAsync($"{url.TrimEnd('/')}/api/health", ct);
			var body = await response.Content.ReadAsStringAsync(ct);

			Console.WriteLine($"Status : {(int)response.StatusCode} {response.StatusCode}");
			Console.WriteLine($"Body   : {body}");

			return response.IsSuccessStatusCode ? 0 : 1;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error  : {ex.Message}");
			return 1;
		}
	}
}
