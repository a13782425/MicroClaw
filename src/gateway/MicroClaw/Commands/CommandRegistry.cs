using System.CommandLine;

namespace MicroClaw.Commands;

public static class CommandRegistry
{
    public static RootCommand Build()
    {
        var root = new RootCommand("MicroClaw - AI Agent 控制面板");

        root.Subcommands.Add(new ServeCommand());
        root.Subcommands.Add(new InitCommand());
        root.Subcommands.Add(new HealthCommand());
        root.Subcommands.Add(new GatewayCommand());
        root.Subcommands.Add(new ProviderCommand());
        root.Subcommands.Add(new ChannelCommand());

        return root;
    }
}
