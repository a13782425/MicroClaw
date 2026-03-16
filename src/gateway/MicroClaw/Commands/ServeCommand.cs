using System.CommandLine;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MicroClaw.Channel.Abstractions;
using MicroClaw.Channel.Feishu;
using MicroClaw.Channel.WeChat;
using MicroClaw.Channel.WeCom;
using MicroClaw.Configuration;
using MicroClaw.Endpoints;
using MicroClaw.Hubs;
using MicroClaw.Provider.Claude;
using MicroClaw.Provider.OpenAI;
using MicroClaw.Providers;
using Serilog;

namespace MicroClaw.Commands;

public class ServeCommand : Command
{
	public ServeCommand() : base("serve", "启动 MicroClaw 服务（默认行为）")
	{
		SetAction(async (ParseResult _, CancellationToken ct) =>
		{
			await RunAsync(ct);
			return 0;
		});
	}

	internal static async Task RunAsync(CancellationToken ct = default)
	{
		var webRootPath = Environment.GetEnvironmentVariable("MICROCLAW_WEBUI_PATH");
		if (string.IsNullOrWhiteSpace(webRootPath))
			webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

		var options = new WebApplicationOptions
		{
			WebRootPath = Directory.Exists(webRootPath) ? webRootPath : null
		};

		var builder = WebApplication.CreateBuilder(options);

		var configFile = Environment.GetEnvironmentVariable("MICROCLAW_CONFIG_FILE");
		if (!string.IsNullOrWhiteSpace(configFile))
			builder.Configuration.AddMicroClawYaml(configFile);

		builder.Host.UseSerilog((ctx, cfg) =>
			cfg.ReadFrom.Configuration(ctx.Configuration));

	builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("auth"));

	var jwtSecret = builder.Configuration["auth:jwt_secret"] ?? "";
	builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
		.AddJwtBearer(opts =>
		{
			opts.TokenValidationParameters = new TokenValidationParameters
			{
				ValidateIssuerSigningKey = true,
				IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
				ValidateIssuer = false,
				ValidateAudience = false,
				ClockSkew = TimeSpan.Zero
			};
		});
	builder.Services.AddAuthorization();

	builder.Services.AddEndpointsApiExplorer();
	builder.Services.AddSwaggerGen();
	builder.Services.AddSignalR();

		var providerRegistry = new ProviderRegistry();
		builder.Services.AddSingleton(providerRegistry);

		var enabledProviders = builder.Configuration.GetSection("Features:Providers").Get<string[]>() ?? [];
		var enabledChannels = builder.Configuration.GetSection("Features:Channels").Get<string[]>() ?? [];

		if (enabledProviders.Contains("OpenAI", StringComparer.OrdinalIgnoreCase))
		{
			builder.Services.AddOpenAIChatClient(builder.Configuration);
			providerRegistry.Register(new ProviderInfo(
				Name: "OpenAI",
				ModelId: builder.Configuration["Providers:OpenAI:ModelId"] ?? "gpt-4o-mini",
				ServiceKey: OpenAIServiceExtensions.ServiceKey));
		}

		if (enabledProviders.Contains("Claude", StringComparer.OrdinalIgnoreCase))
		{
			builder.Services.AddClaudeChatClient(builder.Configuration);
			providerRegistry.Register(new ProviderInfo(
				Name: "Claude",
				ModelId: builder.Configuration["Providers:Claude:ModelId"] ?? "claude-opus-4-5",
				ServiceKey: ClaudeServiceExtensions.ServiceKey));
		}

		if (enabledChannels.Contains("Feishu", StringComparer.OrdinalIgnoreCase))
			builder.Services.AddSingleton<IChannel, FeishuChannel>();

		if (enabledChannels.Contains("WeCom", StringComparer.OrdinalIgnoreCase))
			builder.Services.AddSingleton<IChannel, WeComChannel>();

		if (enabledChannels.Contains("WeChat", StringComparer.OrdinalIgnoreCase))
			builder.Services.AddSingleton<IChannel, WeChatChannel>();

		var app = builder.Build();

	app.UseSerilogRequestLogging();

	app.UseAuthentication();
	app.UseAuthorization();

	if (app.Environment.IsDevelopment())
		{
			app.UseSwagger();
			app.UseSwaggerUI();
		}

		app.UseDefaultFiles();
		app.UseStaticFiles();
		app.MapGatewayEndpoints();
		app.MapHub<GatewayHub>("/ws/gateway");
		app.MapFallbackToFile("index.html");

		await app.RunAsync(ct);
	}
}
