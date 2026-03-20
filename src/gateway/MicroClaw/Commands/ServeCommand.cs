using System.CommandLine;
using System.Text;
using MicroClaw.Agent;
using MicroClaw.Agent.Memory;
using MicroClaw.Channels;
using MicroClaw.Channels.Feishu;
using MicroClaw.Channels.WeChat;
using MicroClaw.Channels.WeCom;
using Microsoft.AspNetCore.StaticFiles;
using MicroClaw.Configuration;
using MicroClaw.Skills;
using MicroClaw.Endpoints;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Hubs;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Jobs;
using MicroClaw.Providers;
using MicroClaw.Providers.Claude;
using MicroClaw.Providers.OpenAI;
using MicroClaw.Sessions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using Serilog;
using Serilog.Events;

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

	/// <summary>启动 Web 服务器，完成环境初始化、服务注册、中间件配置后运行 ASP.NET Core 应用。</summary>
	internal static async Task RunAsync(CancellationToken ct = default)
	{
		var (home, configFile) = InitializeEnvironment();

		var webRootPath = ResolveWebRootPath();
		var options = new WebApplicationOptions
		{
			WebRootPath = Directory.Exists(webRootPath) ? webRootPath : null
		};

		var builder = WebApplication.CreateBuilder(options);

		if (!string.IsNullOrWhiteSpace(configFile))
			builder.Configuration.AddMicroClawYaml(configFile);

		ConfigureLogging(builder, home, configFile);
		ConfigureAuth(builder);
		ConfigureServices(builder, home, configFile);
		ConfigureChannels(builder);

		var app = builder.Build();

		MigrateDatabase(app);
		SeedDefaultAgent(app);
		ConfigureMiddleware(app);
		MapEndpoints(app);

		await app.RunAsync(ct);
	}

	/// <summary>解析 MICROCLAW_HOME / MICROCLAW_CONFIG_FILE 环境变量，加载 .env 文件，并设置 ASPNETCORE_URLS 监听地址。</summary>
	private static (string? home, string? configFile) InitializeEnvironment()
	{
		var home = Environment.GetEnvironmentVariable("MICROCLAW_HOME");
		var configFile = Environment.GetEnvironmentVariable("MICROCLAW_CONFIG_FILE");
		if (string.IsNullOrWhiteSpace(configFile) && !string.IsNullOrWhiteSpace(home))
			configFile = Path.Combine(home, "microclaw.yaml");

		// 确保工作目录和默认配置文件存在（不覆盖用户已有文件）
		HomeInitializer.EnsureInitialized(home, configFile, force: false, verbose: false);

		if (!string.IsNullOrWhiteSpace(home))
			LoadDotEnv(Path.Combine(home, ".env"));

		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
		{
			var gatewayHost = Environment.GetEnvironmentVariable("GATEWAY_HOST") ?? "localhost";
			var gatewayPort = Environment.GetEnvironmentVariable("GATEWAY_PORT") ?? "5080";
			Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://{gatewayHost}:{gatewayPort}");
		}

		return (home, configFile);
	}

	/// <summary>读取 MICROCLAW_WEBUI_PATH，若未设置则默认使用当前目录下的 wwwroot。</summary>
	private static string ResolveWebRootPath()
	{
		var webRootPath = Environment.GetEnvironmentVariable("MICROCLAW_WEBUI_PATH");
		if (string.IsNullOrWhiteSpace(webRootPath))
			webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
		return webRootPath;
	}

	/// <summary>配置 Serilog 结构化日志，输出到控制台和滚动日志文件，最低级别和模板均可由配置文件覆盖。</summary>
	private static void ConfigureLogging(WebApplicationBuilder builder, string? home, string? configFile)
	{
		builder.Host.UseSerilog((ctx, lc) =>
		{
			IConfiguration cfg = ctx.Configuration;
			string logFilePath = ResolveLogFilePath(home, configFile);
			string consoleTemplate = cfg["serilog:write_to:0:args:output_template"]
				?? "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
			string fileTemplate = cfg["serilog:write_to:1:args:output_template"]
				?? "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

			lc.MinimumLevel.Is(ParseLevel(cfg["serilog:minimum_level:default"], LogEventLevel.Information))
			  .MinimumLevel.Override("Microsoft.AspNetCore",
				  ParseLevel(cfg["serilog:minimum_level:override:microsoft.aspnetcore"], LogEventLevel.Warning))
			  .MinimumLevel.Override("Microsoft.Extensions.AI",
				  ParseLevel(cfg["serilog:minimum_level:override:microsoft.extensions.ai"], LogEventLevel.Debug))
			  .Enrich.FromLogContext()
			  .Enrich.WithMachineName()
			  .Enrich.WithThreadId()
			  .WriteTo.Console(outputTemplate: consoleTemplate)
			  .WriteTo.File(logFilePath,
				  rollingInterval: RollingInterval.Day,
				  retainedFileCountLimit: 7,
				  outputTemplate: fileTemplate);
		});
	}

	/// <summary>注册 JWT Bearer 认证和 RBAC 授权，签名密钥从配置项 auth:jwt_secret 读取。</summary>
	private static void ConfigureAuth(WebApplicationBuilder builder)
	{
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
	}

	/// <summary>注册核心基础设施服务：SQLite DbContext、SessionStore、ProviderConfigStore、各 ModelProvider、SignalR 和 Swagger。</summary>
	private static void ConfigureServices(WebApplicationBuilder builder, string? home, string? configFile)
	{
		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen();
		builder.Services.AddSignalR();

		// SQLite 数据库路径（会话元数据 + Provider 配置）
		string dbPath = ResolveDatabasePath(home, configFile);
		string sessionsDir = ResolveSessionsDir(home, configFile);
		builder.Services.AddDbContextFactory<GatewayDbContext>(opts =>
			opts.UseSqlite($"Data Source={dbPath}"));

		builder.Services.AddSingleton<ProviderConfigStore>();
		builder.Services.AddSingleton<SessionStore>(sp =>
			new SessionStore(sp.GetRequiredService<IDbContextFactory<GatewayDbContext>>(), sessionsDir));
		builder.Services.AddSingleton<ISessionReader>(sp => sp.GetRequiredService<SessionStore>());
		builder.Services.AddSingleton<IChannelSessionService, ChannelSessionService>();

		builder.Services.AddSingleton<IModelProvider, OpenAIModelProvider>();
		builder.Services.AddSingleton<IModelProvider, AnthropicModelProvider>();
		builder.Services.AddSingleton<ProviderClientFactory>();

		// Agent 服务
		string agentsDataDir = ResolveAgentsDataDir(home, configFile);
		builder.Services.AddSingleton<AgentStore>();
		builder.Services.AddSingleton<DNAService>(_ => new DNAService(agentsDataDir));
		// 使用工厂注册 ISubAgentRunner，通过 Lazy<AgentRunner> 打破循环依赖
		builder.Services.AddSingleton<ISubAgentRunner>(sp => new SubAgentRunnerService(
			sp.GetRequiredService<SessionStore>(),
			sp.GetRequiredService<AgentStore>(),
			new Lazy<AgentRunner>(() => sp.GetRequiredService<AgentRunner>())));
		builder.Services.AddSingleton<AgentRunner>();
		builder.Services.AddSingleton<IAgentMessageHandler>(sp => sp.GetRequiredService<AgentRunner>());

		// Skills 服务
		string workspaceRoot = ResolveWorkspaceRoot(home, configFile);
		builder.Services.AddSingleton<SkillStore>();
		builder.Services.AddSingleton<SkillService>(_ => new SkillService(workspaceRoot));
		builder.Services.AddSingleton<SkillRunner>();
		builder.Services.AddSingleton<SkillToolFactory>(sp => new SkillToolFactory(
			sp.GetRequiredService<SkillStore>(),
			sp.GetRequiredService<SkillService>(),
			sp.GetRequiredService<SkillRunner>(),
			workspaceRoot));

		// Quartz.NET 定时任务调度
		builder.Services.AddQuartz();
		builder.Services.AddQuartzHostedService(opt =>
		{
			opt.WaitForJobsToComplete = true;
		});
		builder.Services.AddSingleton<CronJobStore>();
		builder.Services.AddSingleton<SessionChatService>();
		builder.Services.AddSingleton<CronJobScheduler>();
		builder.Services.AddSingleton<ICronJobScheduler>(sp => sp.GetRequiredService<CronJobScheduler>());
		builder.Services.AddHostedService<CronJobStartupService>();
	}

	/// <summary>注册渠道配置存储和渠道实现（飞书、企业微信、微信），渠道配置由数据库管理。</summary>
	private static void ConfigureChannels(WebApplicationBuilder builder)
	{
		builder.Services.AddSingleton<ChannelConfigStore>();

		// 飞书：共享消息处理器 + Webhook 渠道 + WebSocket 长连接管理器
		builder.Services.AddSingleton<FeishuMessageProcessor>();
		builder.Services.AddSingleton<IChannel, FeishuChannel>();
		builder.Services.AddHostedService<FeishuWebSocketManager>();

		builder.Services.AddSingleton<IChannel, WeComChannel>();
		builder.Services.AddSingleton<IChannel, WeChatChannel>();
	}

	/// <summary>在应用启动时执行 EF Core 迁移，确保数据库 schema 与当前模型一致。</summary>
	private static void MigrateDatabase(WebApplication app)
	{
		using var scope = app.Services.CreateScope();
		var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<GatewayDbContext>>();
		using var db = dbFactory.CreateDbContext();
		db.Database.Migrate();
	}

	/// <summary>确保系统默认代理（main）存在。</summary>
	private static void SeedDefaultAgent(WebApplication app)
	{
		using var scope = app.Services.CreateScope();
		var agentStore = scope.ServiceProvider.GetRequiredService<AgentStore>();
		agentStore.EnsureMainAgent();
	}

	/// <summary>配置中间件管道：请求日志、认证授权、Swagger UI、默认文件、Brotli 预压缩及静态文件服务。</summary>
	private static void ConfigureMiddleware(WebApplication app)
	{
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
	}

	/// <summary>映射 REST API 端点、SignalR Hub（/ws/gateway）以及 SPA 回退路由（index.html）。</summary>
	private static void MapEndpoints(WebApplication app)
	{
		app.MapGatewayEndpoints();
		app.MapHub<GatewayHub>("/ws/gateway");
		app.MapFallbackToFile("index.html");
	}

	/// <summary>根据 home 或 configFile 路径确定日志目录，返回带日期占位符的滚动日志文件路径。</summary>
	private static string ResolveLogFilePath(string? home, string? configFile)
	{
		string logsDir;
		if (!string.IsNullOrWhiteSpace(home))
			logsDir = Path.Combine(home, "logs");
		else if (!string.IsNullOrWhiteSpace(configFile))
			logsDir = Path.Combine(Path.GetDirectoryName(configFile)!, "logs");
		else
			logsDir = Path.Combine(Directory.GetCurrentDirectory(), ".microclaw", "logs");

		Directory.CreateDirectory(logsDir);
		return Path.Combine(logsDir, "microclaw-.log");
	}

	/// <summary>将字符串解析为 Serilog 日志级别，解析失败时返回 fallback。</summary>
	private static LogEventLevel ParseLevel(string? value, LogEventLevel fallback) =>
		Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out LogEventLevel result)
			? result
			: fallback;

	/// <summary>确定 SQLite 数据库文件的存放目录（优先 home，其次 configFile 同级，最后 .microclaw/），返回完整路径。</summary>
	private static string ResolveDatabasePath(string? home, string? configFile)
	{
		string dir;
		if (!string.IsNullOrWhiteSpace(home))
			dir = home;
		else if (!string.IsNullOrWhiteSpace(configFile))
			dir = Path.GetDirectoryName(Path.GetFullPath(configFile))!;
		else
			dir = Path.Combine(Directory.GetCurrentDirectory(), ".microclaw");

		Directory.CreateDirectory(dir);
		return Path.Combine(dir, "microclaw.db");
	}

	/// <summary>返回会话消息历史的存储目录路径（workspace/sessions/）。</summary>
	private static string ResolveSessionsDir(string? home, string? configFile)
	{
		if (!string.IsNullOrWhiteSpace(home))
			return Path.Combine(home, "workspace", "sessions");
		if (!string.IsNullOrWhiteSpace(configFile))
			return Path.Combine(Path.GetDirectoryName(configFile)!, "workspace", "sessions");
		return Path.Combine(Directory.GetCurrentDirectory(), ".microclaw", "workspace", "sessions");
	}

	/// <summary>返回 Agent DNA 基因文件的存储根目录路径（workspace/agents/）。</summary>
	private static string ResolveAgentsDataDir(string? home, string? configFile)
	{
		if (!string.IsNullOrWhiteSpace(home))
			return Path.Combine(home, "workspace", "agents");
		if (!string.IsNullOrWhiteSpace(configFile))
			return Path.Combine(Path.GetDirectoryName(configFile)!, "workspace", "agents");
		return Path.Combine(Directory.GetCurrentDirectory(), ".microclaw", "workspace", "agents");
	}

	/// <summary>返回 workspace 根目录路径，供 Skills 等子目录使用。</summary>
	private static string ResolveWorkspaceRoot(string? home, string? configFile)
	{
		if (!string.IsNullOrWhiteSpace(home))
			return Path.Combine(home, "workspace");
		if (!string.IsNullOrWhiteSpace(configFile))
			return Path.Combine(Path.GetDirectoryName(configFile)!, "workspace");
		return Path.Combine(Directory.GetCurrentDirectory(), ".microclaw", "workspace");
	}

	/// <summary>根据原始文件扩展名为 Brotli 压缩响应设置正确的 Content-Type 头。</summary>
	private static void SetBrotliContentType(HttpResponse response, string path)
	{
		if (path.EndsWith(".js"))
			response.ContentType = "application/javascript; charset=utf-8";
		else if (path.EndsWith(".css"))
			response.ContentType = "text/css; charset=utf-8";
	}

	/// <summary>解析指定路径的 .env 文件，将键值对写入进程环境变量（已存在的变量不覆盖）。</summary>
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
