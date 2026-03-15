using MicroClaw.Channel.Abstractions;
using MicroClaw.Channel.Feishu;
using MicroClaw.Channel.WeChat;
using MicroClaw.Channel.WeCom;
using MicroClaw.Gateway.Contracts.Auth;
using MicroClaw.Gateway.Hub.Hubs;
using MicroClaw.Gateway.WebApi.Endpoints;
using MicroClaw.Provider.Abstractions;
using MicroClaw.Provider.Claude;
using MicroClaw.Provider.OpenAI;

public class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen();
		builder.Services.AddSignalR();
		builder.Services.AddCors(options =>
		{
			options.AddPolicy("webui", policy =>
			{
				policy
					.WithOrigins("http://localhost:5173")
					.AllowAnyHeader()
					.AllowAnyMethod()
					.AllowCredentials();
			});
		});

		var enabledProviders = builder.Configuration.GetSection("Features:Providers").Get<string[]>() ?? [];
		var enabledChannels = builder.Configuration.GetSection("Features:Channels").Get<string[]>() ?? [];

		if (enabledProviders.Contains("OpenAI", StringComparer.OrdinalIgnoreCase))
		{
			builder.Services.AddSingleton<IModelProvider, OpenAiModelProvider>();
		}

		if (enabledProviders.Contains("Claude", StringComparer.OrdinalIgnoreCase))
		{
			builder.Services.AddSingleton<IModelProvider, ClaudeModelProvider>();
		}

		if (enabledChannels.Contains("Feishu", StringComparer.OrdinalIgnoreCase))
		{
			builder.Services.AddSingleton<IChannel, FeishuChannel>();
		}

		if (enabledChannels.Contains("WeCom", StringComparer.OrdinalIgnoreCase))
		{
			builder.Services.AddSingleton<IChannel, WeComChannel>();
		}

		if (enabledChannels.Contains("WeChat", StringComparer.OrdinalIgnoreCase))
		{
			builder.Services.AddSingleton<IChannel, WeChatChannel>();
		}

		var app = builder.Build();

		if (app.Environment.IsDevelopment())
		{
			app.UseSwagger();
			app.UseSwaggerUI();
		}

		app.UseCors("webui");
		app.UseDefaultFiles();
		app.UseStaticFiles();

		app.MapGatewayEndpoints();

		app.MapHub<GatewayHub>("/ws/gateway");
		app.MapFallbackToFile("index.html");

		app.Run();
	}
}
