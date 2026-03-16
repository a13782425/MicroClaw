using MicroClaw.Commands;

public class Program
{
	public static Task<int> Main(string[] args)
		=> CommandRegistry.Build().Parse(args).InvokeAsync();
}
