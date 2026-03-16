using System.CommandLine;

namespace MicroClaw.Commands;

public static class CommandRegistry
{
    public static RootCommand Build()
    {
        var root = new RootCommand("MicroClaw - AI Agent 控制面板");

        root.Subcommands.Add(new HealthCommand());
        root.Subcommands.Add(new ServeCommand());

        // 无子命令时默认启动服务
        root.SetAction(async (ParseResult _, CancellationToken ct) =>
        {
            await ServeCommand.RunAsync(ct);
            return 0;
        });

        return root;
    }
}
