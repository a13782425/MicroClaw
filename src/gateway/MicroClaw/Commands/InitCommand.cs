using System.CommandLine;
using MicroClaw.Configuration;

namespace MicroClaw.Commands;

public class InitCommand : Command
{
	public InitCommand() : base("init", "初始化 MicroClaw 工作目录（创建目录结构和默认配置文件）")
	{
		var homeOption = new Option<string?>("--home")
		{
			Description = "工作目录路径（默认：$MICROCLAW_HOME 或 ./.microclaw）"
		};

		var forceOption = new Option<bool>("--force")
		{
			Description = "覆盖已存在的主配置文件和 .env 示例文件"
		};
		forceOption.Aliases.Add("-f");

		Options.Add(homeOption);
		Options.Add(forceOption);

		SetAction(async (ParseResult result, CancellationToken _) =>
		{
			string? home = result.GetValue(homeOption)
				?? Environment.GetEnvironmentVariable("MICROCLAW_HOME");
			string? configFile = Environment.GetEnvironmentVariable("MICROCLAW_CONFIG_FILE");
			HomeInitializer.EnsureConsistentHomeAndConfigFile(home, configFile);
			bool force = result.GetValue(forceOption);

			string resolvedHome = HomeInitializer.ResolveHome(home, configFile);
			Console.WriteLine($"初始化工作目录：{resolvedHome}");
			Console.WriteLine();

			HomeInitializer.EnsureInitialized(home, configFile, force, verbose: true);

			Console.WriteLine();
			Console.WriteLine("完成。首次启动 serve 或首次读取认证配置时会自动生成 config/auth.yaml，请修改其中的默认 password 和 jwt_secret。");
			return await Task.FromResult(0);
		});
	}
}
