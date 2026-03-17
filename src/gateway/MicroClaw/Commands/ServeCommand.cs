using System.CommandLine;
using System.Text;
using MicroClaw.Channel.Abstractions;
using Microsoft.AspNetCore.StaticFiles;
using MicroClaw.Channel.Feishu;
using MicroClaw.Channel.WeChat;
using MicroClaw.Channel.WeCom;
using MicroClaw.Configuration;
using MicroClaw.Endpoints;
using MicroClaw.Hubs;
using MicroClaw.Providers;
using MicroClaw.Sessions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
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
		var home = Environment.GetEnvironmentVariable("MICROCLAW_HOME");
		if (!string.IsNullOrWhiteSpace(home))
			LoadDotEnv(Path.Combine(home, ".env"));

		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
		{
			var gatewayHost = Environment.GetEnvironmentVariable("GATEWAY_HOST") ?? "localhost";
			var gatewayPort = Environment.GetEnvironmentVariable("GATEWAY_PORT") ?? "5080";
			Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://{gatewayHost}:{gatewayPort}");
		}

		var webRootPath = Environment.GetEnvironmentVariable("MICROCLAW_WEBUI_PATH");
		if (string.IsNullOrWhiteSpace(webRootPath))
			webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

		var options = new WebApplicationOptions
		{
			WebRootPath = Directory.Exists(webRootPath) ? webRootPath : null
		};

		var builder = WebApplication.CreateBuilder(options);

		var configFile = Environment.GetEnvironmentVariable("MICROCLAW_CONFIG_FILE");
		if (string.IsNullOrWhiteSpace(configFile) && !string.IsNullOrWhiteSpace(home))
			configFile = Path.Combine(home, "microclaw.yaml");
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

		// Determine providers config file path
		string providersYamlPath = ResolveProvidersYamlPath(home, configFile);
		string sessionsDir = ResolveSessionsDir(home, configFile);

		builder.Services.AddSingleton(new ProviderConfigStore(providersYamlPath));
		builder.Services.AddSingleton(new SessionStore(sessionsDir));
		builder.Services.AddSingleton<ProviderClientFactory>();

		var enabledChannels = builder.Configuration.GetSection("Features:Channels").Get<string[]>() ?? [];

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

		// Brotli 静态预压缩中间件，必须在 UseStaticFiles 之前
		app.Use(async (context, next) =>
		{
			string requestPath = context.Request.Path.Value ?? "";
			bool isGetOrHead   = context.Request.Method is "GET" or "HEAD";
			bool isAsset       = requestPath.StartsWith("/assets/");

			if (isGetOrHead && isAsset)
			{
				string acceptEncoding  = context.Request.Headers.AcceptEncoding.ToString();
				bool isBrotliSupported = acceptEncoding.Contains("br");

				if (!requestPath.EndsWith(".br") && isBrotliSupported)
				{
					// 正常请求 → 尝试内部重写到 .br 文件
					IWebHostEnvironment env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
					string brPath           = requestPath + ".br";
					IFileInfo brFile        = env.WebRootFileProvider.GetFileInfo(brPath);

					if (brFile.Exists)
					{
						PathString originalPath  = context.Request.Path;
						context.Request.Path     = new PathString(brPath);
						context.Response.OnStarting(() =>
						{
							context.Response.Headers.ContentEncoding = "br";
							context.Response.Headers.Vary            = "Accept-Encoding";
							SetBrotliContentType(context.Response, originalPath.Value!);
							return Task.CompletedTask;
						});
					}
				}
				else if (requestPath.EndsWith(".br"))
				{
					// Chrome 直接探测 .br URL（投机性预加载）→ 补全 Content-Encoding 响应头
					string orig = requestPath[..^3];
					context.Response.OnStarting(() =>
					{
						context.Response.Headers.ContentEncoding = "br";
						context.Response.Headers.Vary            = "Accept-Encoding";
						SetBrotliContentType(context.Response, orig);
						return Task.CompletedTask;
					});
				}
			}

			await next();
		});

		var contentTypeProvider = new FileExtensionContentTypeProvider();
		contentTypeProvider.Mappings[".br"] = "application/octet-stream"; // 占位，响应头由中间件覆盖

		app.UseStaticFiles(new StaticFileOptions
		{
			ContentTypeProvider = contentTypeProvider,
			OnPrepareResponse   = ctx =>
			{
				string name = ctx.File.Name;
				if (name.EndsWith(".js") || name.EndsWith(".css") || name.EndsWith(".br"))
					ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
				else if (name.EndsWith(".html"))
					ctx.Context.Response.Headers.CacheControl = "no-cache";
			}
		});
        app.MapGatewayEndpoints();
		app.MapHub<GatewayHub>("/ws/gateway");
		app.MapFallbackToFile("index.html");

		await app.RunAsync(ct);
	}

	private static string ResolveProvidersYamlPath(string? home, string? configFile)
	{
		if (!string.IsNullOrWhiteSpace(home))
			return Path.Combine(home, "config", "providers.yaml");

		if (!string.IsNullOrWhiteSpace(configFile))
			return Path.Combine(Path.GetDirectoryName(configFile)!, "config", "providers.yaml");

		return Path.Combine(Directory.GetCurrentDirectory(), ".microclaw", "config", "providers.yaml");
	}

	private static string ResolveSessionsDir(string? home, string? configFile)
	{
		if (!string.IsNullOrWhiteSpace(home))
			return Path.Combine(home, "workspace", "sessions");

		if (!string.IsNullOrWhiteSpace(configFile))
			return Path.Combine(Path.GetDirectoryName(configFile)!, "workspace", "sessions");

		return Path.Combine(Directory.GetCurrentDirectory(), ".microclaw", "workspace", "sessions");
	}

	private static void SetBrotliContentType(HttpResponse response, string path)
	{
		if (path.EndsWith(".js"))
			response.ContentType = "application/javascript; charset=utf-8";
		else if (path.EndsWith(".css"))
			response.ContentType = "text/css; charset=utf-8";
	}

	private static void LoadDotEnv(string path)
	{
		if (!File.Exists(path)) return;
		foreach (var line in File.ReadAllLines(path))
		{
			var trimmed = line.Trim();
			if (trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;
			var idx = trimmed.IndexOf('=');
			var key = trimmed[..idx].Trim();
			var value = trimmed[(idx + 1)..].Trim().Trim('"').Trim('\'');
			if (!string.IsNullOrEmpty(key) && Environment.GetEnvironmentVariable(key) is null)
				Environment.SetEnvironmentVariable(key, value);
		}
	}
}
