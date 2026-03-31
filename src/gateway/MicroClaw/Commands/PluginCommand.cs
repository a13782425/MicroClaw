using System.CommandLine;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MicroClaw.Commands;

public class PluginCommand : Command
{
	public PluginCommand() : base("plugin", "管理插件")
	{
		Subcommands.Add(new PluginListCommand());
		Subcommands.Add(new PluginInstallCommand());
		Subcommands.Add(new PluginEnableCommand());
		Subcommands.Add(new PluginDisableCommand());
		Subcommands.Add(new PluginUpdateCommand());
		Subcommands.Add(new PluginUninstallCommand());
		Subcommands.Add(new PluginReloadCommand());
	}

	public static Option<string> UrlOption() => new("--url")
	{
		Description = "目标服务地址",
		DefaultValueFactory = _ => "http://localhost:5080"
	};

	public static Option<string?> TokenOption() => new("--token")
	{
		Description = "JWT 认证令牌"
	};

	public static HttpClient CreateHttpClient(string? token)
	{
		var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		if (!string.IsNullOrWhiteSpace(token))
			http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
		return http;
	}
}

public class PluginListCommand : Command
{
	public PluginListCommand() : base("list", "列出所有已安装的插件")
	{
		var urlOpt = PluginCommand.UrlOption();
		var tokenOpt = PluginCommand.TokenOption();
		Options.Add(urlOpt);
		Options.Add(tokenOpt);

		SetAction(async (ParseResult pr, CancellationToken ct) =>
		{
			using var http = PluginCommand.CreateHttpClient(pr.GetValue(tokenOpt));
			try
			{
				var resp = await http.GetAsync($"{pr.GetValue(urlOpt)!.TrimEnd('/')}/api/plugins", ct);
				var body = await resp.Content.ReadAsStringAsync(ct);
				Console.WriteLine(body);
				return resp.IsSuccessStatusCode ? 0 : 1;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"错误: {ex.Message}");
				return 1;
			}
		});
	}
}

public class PluginInstallCommand : Command
{
	public PluginInstallCommand() : base("install", "安装插件（从 Git 仓库）")
	{
		var urlOpt = PluginCommand.UrlOption();
		var tokenOpt = PluginCommand.TokenOption();
		var repoArg = new Argument<string>("repo") { Description = "Git 仓库 URL" };
		var refOpt = new Option<string?>("--ref") { Description = "Git 分支或标签" };
		Options.Add(urlOpt);
		Options.Add(tokenOpt);
		Options.Add(refOpt);
		Arguments.Add(repoArg);

		SetAction(async (ParseResult pr, CancellationToken ct) =>
		{
			using var http = PluginCommand.CreateHttpClient(pr.GetValue(tokenOpt));
			try
			{
				var payload = JsonSerializer.Serialize(new { url = pr.GetValue(repoArg), @ref = pr.GetValue(refOpt) });
				var content = new StringContent(payload, Encoding.UTF8, "application/json");
				var resp = await http.PostAsync($"{pr.GetValue(urlOpt)!.TrimEnd('/')}/api/plugins/install", content, ct);
				var body = await resp.Content.ReadAsStringAsync(ct);
				Console.WriteLine(body);
				return resp.IsSuccessStatusCode ? 0 : 1;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"错误: {ex.Message}");
				return 1;
			}
		});
	}
}

public class PluginEnableCommand : Command
{
	public PluginEnableCommand() : base("enable", "启用插件")
	{
		var urlOpt = PluginCommand.UrlOption();
		var tokenOpt = PluginCommand.TokenOption();
		var nameArg = new Argument<string>("name") { Description = "插件名称" };
		Options.Add(urlOpt);
		Options.Add(tokenOpt);
		Arguments.Add(nameArg);

		SetAction(async (ParseResult pr, CancellationToken ct) =>
		{
			using var http = PluginCommand.CreateHttpClient(pr.GetValue(tokenOpt));
			try
			{
				var resp = await http.PostAsync($"{pr.GetValue(urlOpt)!.TrimEnd('/')}/api/plugins/{pr.GetValue(nameArg)}/enable", null, ct);
				Console.WriteLine(resp.IsSuccessStatusCode ? "已启用" : $"失败: {resp.StatusCode}");
				return resp.IsSuccessStatusCode ? 0 : 1;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"错误: {ex.Message}");
				return 1;
			}
		});
	}
}

public class PluginDisableCommand : Command
{
	public PluginDisableCommand() : base("disable", "禁用插件")
	{
		var urlOpt = PluginCommand.UrlOption();
		var tokenOpt = PluginCommand.TokenOption();
		var nameArg = new Argument<string>("name") { Description = "插件名称" };
		Options.Add(urlOpt);
		Options.Add(tokenOpt);
		Arguments.Add(nameArg);

		SetAction(async (ParseResult pr, CancellationToken ct) =>
		{
			using var http = PluginCommand.CreateHttpClient(pr.GetValue(tokenOpt));
			try
			{
				var resp = await http.PostAsync($"{pr.GetValue(urlOpt)!.TrimEnd('/')}/api/plugins/{pr.GetValue(nameArg)}/disable", null, ct);
				Console.WriteLine(resp.IsSuccessStatusCode ? "已禁用" : $"失败: {resp.StatusCode}");
				return resp.IsSuccessStatusCode ? 0 : 1;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"错误: {ex.Message}");
				return 1;
			}
		});
	}
}

public class PluginUpdateCommand : Command
{
	public PluginUpdateCommand() : base("update", "更新插件（git pull）")
	{
		var urlOpt = PluginCommand.UrlOption();
		var tokenOpt = PluginCommand.TokenOption();
		var nameArg = new Argument<string>("name") { Description = "插件名称" };
		Options.Add(urlOpt);
		Options.Add(tokenOpt);
		Arguments.Add(nameArg);

		SetAction(async (ParseResult pr, CancellationToken ct) =>
		{
			using var http = PluginCommand.CreateHttpClient(pr.GetValue(tokenOpt));
			try
			{
				var resp = await http.PostAsync($"{pr.GetValue(urlOpt)!.TrimEnd('/')}/api/plugins/{pr.GetValue(nameArg)}/update", null, ct);
				Console.WriteLine(resp.IsSuccessStatusCode ? "已更新" : $"失败: {resp.StatusCode}");
				return resp.IsSuccessStatusCode ? 0 : 1;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"错误: {ex.Message}");
				return 1;
			}
		});
	}
}

public class PluginUninstallCommand : Command
{
	public PluginUninstallCommand() : base("uninstall", "卸载插件")
	{
		var urlOpt = PluginCommand.UrlOption();
		var tokenOpt = PluginCommand.TokenOption();
		var nameArg = new Argument<string>("name") { Description = "插件名称" };
		Options.Add(urlOpt);
		Options.Add(tokenOpt);
		Arguments.Add(nameArg);

		SetAction(async (ParseResult pr, CancellationToken ct) =>
		{
			using var http = PluginCommand.CreateHttpClient(pr.GetValue(tokenOpt));
			try
			{
				var resp = await http.DeleteAsync($"{pr.GetValue(urlOpt)!.TrimEnd('/')}/api/plugins/{pr.GetValue(nameArg)}", ct);
				Console.WriteLine(resp.IsSuccessStatusCode ? "已卸载" : $"失败: {resp.StatusCode}");
				return resp.IsSuccessStatusCode ? 0 : 1;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"错误: {ex.Message}");
				return 1;
			}
		});
	}
}

public class PluginReloadCommand : Command
{
	public PluginReloadCommand() : base("reload", "重新加载所有插件")
	{
		var urlOpt = PluginCommand.UrlOption();
		var tokenOpt = PluginCommand.TokenOption();
		Options.Add(urlOpt);
		Options.Add(tokenOpt);

		SetAction(async (ParseResult pr, CancellationToken ct) =>
		{
			using var http = PluginCommand.CreateHttpClient(pr.GetValue(tokenOpt));
			try
			{
				var resp = await http.PostAsync($"{pr.GetValue(urlOpt)!.TrimEnd('/')}/api/plugins/reload", null, ct);
				Console.WriteLine(resp.IsSuccessStatusCode ? "已重新加载" : $"失败: {resp.StatusCode}");
				return resp.IsSuccessStatusCode ? 0 : 1;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"错误: {ex.Message}");
				return 1;
			}
		});
	}
}
