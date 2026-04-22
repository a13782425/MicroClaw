using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicroClaw.Abstractions.Events;
using MicroClaw.Agent;
using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Dev;
using MicroClaw.Agent.Memory;
using MicroClaw.Agent.Sessions;
using MicroClaw.Channels;
using MicroClaw.Tools;
using Microsoft.AspNetCore.StaticFiles;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Skills;
using MicroClaw.Endpoints;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Plugins;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Core;
using MicroClaw.Core.Logging;
using MicroClaw.Hubs;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Jobs;
using MicroClaw.Logging;
using MicroClaw.Providers;
using MicroClaw.Pet;
using MicroClaw.Pet.Emotion;
using MicroClaw.Plugins;
using MicroClaw.Plugins.Hooks;
using MicroClaw.Plugins.Marketplace;
using MicroClaw.RAG;
using MicroClaw.Services;
using MicroClaw.Extensions;
using MicroClaw.Sessions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
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
		DotEnvLoader.Load();

		var webRootPath = MicroClawConfig.Env.Get(MICROCLAW_WEBUI_PATH);
		var options = new WebApplicationOptions
		{
			WebRootPath = Directory.Exists(webRootPath) ? webRootPath : null
		};

		var builder = WebApplication.CreateBuilder(options);
		builder.Host.UseDefaultServiceProvider((_, opts) =>
		{
			opts.ValidateScopes = true;
			opts.ValidateOnBuild = true;
		});
		string? configFile = MicroClawConfig.Env.Get(MICROCLAW_CONFIG_FILE);
		if (!string.IsNullOrWhiteSpace(configFile))
			builder.Configuration.AddMicroClawYaml(configFile);
		builder.Configuration.AddEnvironmentVariables();
		builder.Configuration.AddEnvironmentVariables("DOTNET_");

		// 初始化静态配置门面（必须在 YAML 加载之后、使用配置之前）
		MicroClawConfig.Initialize(builder.Configuration, MicroClawConfig.Env.ConfigDir);

		ConfigureLogging(builder);
		ConfigureAuth(builder);
		ConfigureServices(builder);
		ConfigureChannels(builder);
		
		var app = builder.Build();

		MicroLogger.Factory = new MelMicroLoggerFactory(
			app.Services.GetRequiredService<ILoggerFactory>());

		ValidateStartupConfiguration(app);
		ConfigureMiddleware(app);
		MapEndpoints(app);

		await app.RunAsync(ct);
	}

	/// <summary>配置 Serilog 结构化日志，输出到控制台和滚动日志文件，最低级别和模板均可由配置文件覆盖。</summary>
	private static void ConfigureLogging(WebApplicationBuilder builder)
	{
		builder.Host.UseSerilog((ctx, lc) =>
		{
			IConfiguration cfg = ctx.Configuration;
			LoggingOptions options = MicroClawConfig.Get<LoggingOptions>();
			IReadOnlyList<LoggingSinkOptions> sinks = cfg.GetSection("serilog:write_to").Exists()
				? options.WriteTo
				: LoggingOptions.CreateDefaultSinks();
			if (!sinks.Any(static sink => string.Equals(sink.Name, "console", StringComparison.OrdinalIgnoreCase) || string.Equals(sink.Name, "file", StringComparison.OrdinalIgnoreCase)))
			{
				sinks = LoggingOptions.CreateDefaultSinks();
			}

			IReadOnlyList<string> enrichers = cfg.GetSection("serilog:enrich").Exists()
				? options.Enrich
				: LoggingOptions.CreateDefaultEnrichers();
			LoggingSinkOptions? consoleSink = GetLoggingSink(sinks, "console");
			LoggingSinkOptions? fileSink = GetLoggingSink(sinks, "file");
			string consoleTemplate = consoleSink?.Args.OutputTemplate
				?? "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
			string fileTemplate = fileSink?.Args.OutputTemplate
				?? "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
			string logFilePath = ResolveLogFilePath(fileSink?.Args.Path);
			int retainedFileCountLimit = fileSink?.Args.RetainedFileCountLimit ?? 7;
			RollingInterval rollingInterval = ParseRollingInterval(fileSink?.Args.RollingInterval, RollingInterval.Day);

			lc.MinimumLevel.Is(ParseLevel(options.MinimumLevel.Default, LogEventLevel.Information))
			  .MinimumLevel.Override("Microsoft.AspNetCore",
				  ParseLevel(options.MinimumLevel.Override.MicrosoftAspNetCore, LogEventLevel.Warning))
			  .MinimumLevel.Override("Microsoft.Extensions.AI",
				  ParseLevel(options.MinimumLevel.Override.MicrosoftExtensionsAi, LogEventLevel.Debug))
			  .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command",
				  ParseLevel(options.MinimumLevel.Override.MicrosoftEntityFrameworkCoreDatabaseCommand, LogEventLevel.Warning));

			if (enrichers.Contains("from_log_context", StringComparer.OrdinalIgnoreCase))
				lc.Enrich.FromLogContext();
			if (enrichers.Contains("with_machine_name", StringComparer.OrdinalIgnoreCase))
				lc.Enrich.WithMachineName();
			if (enrichers.Contains("with_thread_id", StringComparer.OrdinalIgnoreCase))
				lc.Enrich.WithThreadId();

			if (consoleSink is not null)
			{
				lc.WriteTo.Console(outputTemplate: consoleTemplate);
			}

			if (fileSink is not null)
			{
				lc.WriteTo.File(logFilePath,
					rollingInterval: rollingInterval,
					retainedFileCountLimit: retainedFileCountLimit,
					outputTemplate: fileTemplate);
			}
		});
	}

	/// <summary>注册 JWT Bearer 认证和 RBAC 授权，签名密钥从配置项 auth:jwt_secret 读取。</summary>
	private static void ConfigureAuth(WebApplicationBuilder builder)
	{
		var jwtSecret = MicroClawConfig.Get<AuthOptions>().JwtSecret;
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
				// SignalR WebSocket 握手时浏览器无法附加 Authorization 请求头，
				// JS SDK 会自动将 token 作为 access_token query 参数发送，此处从中读取。
				opts.Events = new JwtBearerEvents
				{
					OnMessageReceived = ctx =>
					{
						var token = ctx.Request.Query["access_token"];
						if (!string.IsNullOrEmpty(token) &&
							ctx.HttpContext.Request.Path.StartsWithSegments("/ws"))
						{
							ctx.Token = token;
						}
						return Task.CompletedTask;
					}
				};
			});
		builder.Services.AddAuthorization();
	}

	/// <summary>注册核心基础设施服务：SQLite DbContext、SessionStore、ProviderService、SignalR 和 Swagger。</summary>
	private static void ConfigureServices(WebApplicationBuilder builder)
	{
		builder.Services.ConfigureHttpJsonOptions(options =>
		{
			options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
			options.SerializerOptions.PropertyNameCaseInsensitive = true;
		});

		builder.Services.AddEndpointsApiExplorer();
		builder.Services.AddSwaggerGen();
		builder.Services.AddSignalR();
		builder.Services.AddDataProtection();
		builder.Services.AddSingleton<MicroClaw.Services.SandboxTokenService>();
		builder.Services.AddHttpClient("fetch", client =>
		{
			client.Timeout = TimeSpan.FromSeconds(30);
			client.DefaultRequestHeaders.UserAgent.ParseAdd("MicroClaw/1.0");
		});

		// SQLite 数据库路径（仅用于 cron_jobs、usages、channel_retry_queue）
		string dbPath = MicroClawConfig.Env.DbPath;
		builder.Services.AddDbContextFactory<GatewayDbContext>(opts =>
		{
			opts.UseSqlite($"Data Source={dbPath}");
			opts.LogTo(_ => {}, LogLevel.None);  // 禁用所有 EF 日志
		});

		builder.Services.AddSingleton<ConfigService>();
		// ProviderService：依赖 IUsageTracker，按 MicroService 生命周期启动（Order=15），
		// 取代旧的 ProviderClientFactory + 直接暴露 IChatClient 的模式。
		builder.Services.AddMicroService<ProviderService>();
		builder.Services.AddMicroService<SessionService>();
		builder.Services.MapAs<ISessionService, SessionService>();
		
		// Agent 服务
		builder.Services.AddService<AgentStore>();
		builder.Services.MapAs<IPluginAgentRegistrar, AgentStore>();
		builder.Services.MapAs<IAgentRepository, AgentStore>();
		builder.Services.AddSingleton<AgentDnaService>();
		builder.Services.AddSingleton<SessionDnaService>();
		builder.Services.AddSingleton<MemoryService>();
		// RAG 服务
		// 旧的 IEmbeddingProvider / ProviderEmbeddingFactory / IEmbeddingProviderAccessor /
		// IEmbeddingService（DynamicEmbeddingService）已整体下线，Embedding 统一由
		// ProviderService.GetDefaultEmbeddingProvider() 驱动 EmbeddingMicroProvider 完成，
		// 同时在 Provider 内部做 usage 记账，无需上层 DI 单独注册。
		builder.Services.AddSingleton<RagReindexJobTracker>();
		builder.Services.AddSingleton<RagReindexService>();
		builder.Services.AddSingleton<RagRetrievalContext>();
		builder.Services.AddSingleton<IRagUsageAuditor, RagUsageAuditor>();
		builder.Services.AddSingleton<IContextOverflowSummarizer, ContextOverflowSummarizer>();
		// Pet 情绪系统服务（基于 Session 隔离，替代原 Agent 级 Emotion 系统）
		builder.Services.AddSingleton<IEmotionStore, EmotionStore>();
		builder.Services.AddSingleton<IEmotionRuleEngine, EmotionRuleEngine>();
		builder.Services.AddSingleton<IEmotionBehaviorMapper, EmotionBehaviorMapper>();
		// Provider 路由器
		builder.Services.AddSingleton<IProviderRouter, ProviderRouter>();
		// Context Providers（按 Order 聚合 System Prompt）
		builder.Services.AddSingleton<IAgentContextProvider, ServerTimeContextProvider>();  // Order 5：服务器时间层
		builder.Services.AddSingleton<IAgentContextProvider, AgentDnaContextProvider>();
		builder.Services.AddSingleton<IAgentContextProvider, RagContextProvider>(); // Order 15：语义检索层
		builder.Services.AddSingleton<IAgentContextProvider, SessionDnaContextProvider>();
		builder.Services.AddSingleton<IAgentContextProvider, SessionMemoryContextProvider>();
		// ISubAgentRunner: 通过 IServiceProvider 懒解析，彻底消除 Lazy<AgentRunner> 循环依赖
		builder.Services.AddSingleton<SubAgentRunnerService>();
		builder.Services.MapAs<ISubAgentRunner, SubAgentRunnerService>();
		builder.Services.AddSingleton<IMicroHubService, MicroHubService>();
		builder.Services.AddSingleton<IAgentStatusNotifier, HubAgentStatusNotifier>();
		// SessionMessage → AIContent 还原服务（Restorer 由 Service 内部构建）
		builder.Services.AddSingleton<MicroClaw.Agent.Restorers.ChatContentRestorerService>();
		builder.Services.AddService<AgentRunner>();
		// P-F-5: Pet 编排层服务注册（Pet 为消息入口，AgentRunner 保留但不再作为消息入口）
		builder.Services.AddSingleton<MicroClaw.Pet.Storage.PetStateStore>();
		builder.Services.AddSingleton<MicroClaw.Pet.RateLimit.PetRateLimiter>();
		builder.Services.AddSingleton<MicroClaw.Pet.Decision.PetModelSelector>();
		builder.Services.AddSingleton<MicroClaw.Pet.Decision.PetDecisionEngine>();
		builder.Services.AddPetStates();
		builder.Services.AddSingleton<MicroClaw.Pet.StateMachine.PetStateMachine>();
		builder.Services.AddSingleton<MicroClaw.Pet.StateMachine.PetSelfAwarenessReportBuilder>();
		builder.Services.AddSingleton<MicroClaw.Pet.Prompt.PetPromptStore>();
		builder.Services.AddSingleton<MicroClaw.Pet.Prompt.PetPromptEvolver>();
		builder.Services.AddSingleton<MicroClaw.Pet.PetContextFactory>();
		builder.Services.AddSingleton<MicroClaw.Pet.PetFactory>();
		builder.Services.MapAs<MicroClaw.Abstractions.Pet.IPetFactory, MicroClaw.Pet.PetFactory>();
		builder.Services.AddSingleton<MicroClaw.Pet.Observer.PetSessionObserver>();
		builder.Services.AddService<MicroClaw.Pet.PetService>();
		builder.Services.MapAs<MicroClaw.Pet.IPetService, MicroClaw.Pet.PetService>();
		// P-F-3: IAgentMessageHandler 指向 PetService，渠道消息经 Pet 编排后再委派 AgentRunner
		builder.Services.MapAs<IAgentMessageHandler, MicroClaw.Pet.PetService>();

		// Workflow 服务
		builder.Services.AddSingleton<MicroClaw.Agent.Workflows.WorkflowStore>();
		builder.Services.AddSingleton<MicroClaw.Agent.Workflows.WorkflowEngine>();

		// 开发调试指标服务（始终注册；调试端点仅在 Development 环境映射）
		builder.Services.AddSingleton<IDevMetricsService, DevMetricsService>();

		// Skills 服务
		builder.Services.AddSingleton<SkillService>();
		builder.Services.MapAs<IPluginSkillRegistrar, SkillService>();
		builder.Services.AddSingleton<SkillStore>();
		builder.Services.AddSingleton<SkillToolFactory>();
		builder.Services.AddSingleton<MicroClaw.Skills.SkillInvocationTool>();
		builder.Services.AddSingleton<MicroClaw.Skills.IAgentLookup, MicroClaw.Services.AgentStoreAgentLookup>();
		builder.Services.AddSingleton<McpServerConfigStore>();

		// D-6: MCP 动态工具注册——运行时注册表，启动时从 DB 同步，API 变更后即时生效，无需重启
		builder.Services.AddService<McpServerRegistry>();
		builder.Services.MapAs<IMcpServerRegistry, McpServerRegistry>();
		builder.Services.MapAs<IPluginMcpRegistrar, McpServerRegistry>();

		// 工具提供者（实现 IToolProvider，ToolCollector 自动发现，无需手动硬编码）
		builder.Services.AddSingleton<IToolProvider, FetchToolProvider>();
		builder.Services.AddSingleton<IToolProvider, ShellToolProvider>();
		builder.Services.AddSingleton<IToolProvider, CronToolProvider>();
		builder.Services.AddSingleton<IToolProvider, SubAgentToolProvider>();
		builder.Services.AddSingleton<MicroClaw.Abstractions.ISandboxUrlGenerator>(sp => sp.GetRequiredService<MicroClaw.Services.SandboxTokenService>());
		builder.Services.AddSingleton<IToolProvider, FileToolProvider>();
		builder.Services.AddSingleton<IToolProvider, SkillToolProvider>();
		builder.Services.AddSingleton<ToolCollector>();

		// 插件系统
		builder.Services.AddService<PluginLoader>();
		builder.Services.MapAs<IPluginRegistry, PluginLoader>();
		builder.Services.AddSingleton<IHookExecutor, HookExecutor>();

		// 插件市场
		builder.Services.AddSingleton<IPluginMarketplace, ClaudeMarketplaceAdapter>();
		builder.Services.AddSingleton<IPluginMarketplace, CopilotMarketplaceAdapter>();
		builder.Services.AddService<MarketplaceManager>();
		builder.Services.MapAs<IMarketplaceManager, MarketplaceManager>();

		// Quartz.NET 定时任务调度
		builder.Services.AddQuartz(q => q.AddJob<SystemJobRunner>(opts => opts.StoreDurably()));
		builder.Services.AddQuartzHostedService(opt =>
		{
			opt.WaitForJobsToComplete = true;
		});
		builder.Services.AddSingleton<CronJobStore>();
		builder.Services.AddSingleton<SessionChatService>();
		builder.Services.AddSingleton<CronJobScheduler>();
		builder.Services.MapAs<ICronJobScheduler, CronJobScheduler>();
		builder.Services.AddHostedService<ServiceLifetimeHost>();
		builder.Services.AddHostedService<CronJobStartupService>();
		builder.Services.AddHostedService<MicroClaw.Pet.PetRunner>();
		builder.Services.AddMicroEngine();

		// Token 用量追踪
		builder.Services.AddSingleton<IUsageTracker, UsageTracker>();

		// F-D-1: 渠道消息失败重试队列
		builder.Services.AddSingleton<ChannelRetryQueueService>();
		builder.Services.MapAs<IChannelRetryQueue, ChannelRetryQueueService>();
		builder.Services.AddSingleton<IScheduledJob, ChannelRetryJob>();
		// P-E-1: Pet 私有 RAG — PetRagScope removed, replaced by MicroRag instances
		// 2-A-11: RAG 定期容量清理（每日 UTC 01:00，早于记忆总结和做梦模式）
		builder.Services.AddSingleton<IScheduledJob, RagPruneJob>();
		// P-B-7: Pet 情绪自然衰减 — 暂禁用，待 Pet 系统重构后适配恢复
		// builder.Services.AddSingleton<IScheduledJob, PetEmotionDecayJob>();
		// P-G: Pet 心跳与自主行为
		builder.Services.AddSingleton<MicroClaw.Pet.Heartbeat.IPetNotifier, MicroClaw.Services.HubPetNotifier>();
		builder.Services.AddSingleton<MicroClaw.Pet.Heartbeat.PetActionExecutor>();
		builder.Services.AddSingleton<MicroClaw.Pet.Heartbeat.PetHeartbeatExecutor>();
		// builder.Services.AddSingleton<IScheduledJob, PetHeartbeatJob>(); // 暂禁用，待 Pet 系统重构后适配恢复

		// 全局异步事件总线
		builder.Services.AddSingleton<IAsyncEventBus, MicroClaw.Events.InMemoryAsyncEventBus>();
	}

	/// <summary>注册渠道配置存储和渠道实现，由 ChannelService 内部构建 Provider，ChannelRunner 统一调度生命周期。</summary>
	private static void ConfigureChannels(WebApplicationBuilder builder)
	{
		builder.Services.AddService<ChannelService>();
		builder.Services.MapAs<IChannelService, ChannelService>();

		// ChannelRunner: 统一驱动所有 Provider 的 StartAsync/TickAsync/StopAsync 生命周期
		builder.Services.AddRunner<ChannelRunner>();

		// ChannelToolBridge: 桥接 IChannelProvider 工具管理到 ToolCollector
		builder.Services.AddSingleton<IToolProvider, ChannelToolBridge>();

		// B-02: 每日记忆总结（将会话消息摘要写入 memory/YYYY-MM-DD.md，每周合并至 MEMORY.md）
		builder.Services.AddSingleton<IScheduledJob, MemorySummarizationJob>();
		builder.Services.AddSingleton<IScheduledJob, MemoryPendingProcessorJob>();
		// D-2: 做梦模式——每日凌晨 3 点，跨会话归因/摘要，将认知整理结果写回 Agent MEMORY.md
		builder.Services.AddSingleton<IScheduledJob, DreamingJob>();
		// 系统 Job 统一调度器
		builder.Services.AddHostedService<SystemJobRegistrar>();


	}

	/// <summary>校验关键配置项安全性，不满足要求时记录 Warning 级别日志。</summary>
	private static void ValidateStartupConfiguration(WebApplication app)
	{
		AuthOptions authOptions = MicroClawConfig.Get<AuthOptions>();
		EnsureAuthConfigurationIsSafe(authOptions);
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
		// if (app.Environment.IsDevelopment())
		app.MapDevEndpoints();
		app.MapHub<GatewayHub>("/ws/gateway");
		app.MapFallbackToFile("index.html");
	}

	/// <summary>根据原始文件扩展名为 Brotli 压缩响应设置正确的 Content-Type 头。</summary>
	private static void SetBrotliContentType(HttpResponse response, string path)
	{
		if (path.EndsWith(".js"))
			response.ContentType = "application/javascript; charset=utf-8";
		else if (path.EndsWith(".css"))
			response.ContentType = "text/css; charset=utf-8";
	}

	/// <summary>将字符串解析为 Serilog 日志级别，解析失败时返回 fallback。</summary>
	private static LogEventLevel ParseLevel(string? value, LogEventLevel fallback) =>
		Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out LogEventLevel result)
			? result
			: fallback;

	internal static void EnsureAuthConfigurationIsSafe(AuthOptions options)
	{
		if (string.Equals(options.Password, AuthOptions.DefaultPassword, StringComparison.Ordinal) ||
			string.Equals(options.JwtSecret, AuthOptions.DefaultJwtSecret, StringComparison.Ordinal))
		{
			throw new InvalidOperationException(
				"检测到默认认证占位值。请先在 auth.yaml 或环境变量中设置自定义 password 和 jwt_secret，再启动服务。");
		}

		int jwtSecretBytes = Encoding.UTF8.GetByteCount(options.JwtSecret);
		if (jwtSecretBytes < 32)
		{
			throw new InvalidOperationException(
				$"JWT secret 强度不足（当前 {jwtSecretBytes} 字节，要求 ≥32 字节）。请在配置项 auth:jwt_secret 中设置 32 个字符以上的强密钥后再启动服务。");
		}
	}

	private static LoggingSinkOptions? GetLoggingSink(IEnumerable<LoggingSinkOptions> sinks, string sinkName)
	{
		return sinks.FirstOrDefault(sink => string.Equals(sink.Name, sinkName, StringComparison.OrdinalIgnoreCase));
	}

	private static string ResolveLogFilePath(string? configuredPath)
	{
		if (string.IsNullOrWhiteSpace(configuredPath))
			return MicroClawConfig.Env.LogFilePath;

		if (Path.IsPathRooted(configuredPath))
			return configuredPath;

		string normalizedRelativePath = configuredPath.Replace('/', Path.DirectorySeparatorChar);
		return Path.Combine(MicroClawConfig.Env.Home, normalizedRelativePath);
	}

	private static RollingInterval ParseRollingInterval(string? value, RollingInterval fallback) =>
		Enum.TryParse<RollingInterval>(value, ignoreCase: true, out RollingInterval result)
			? result
			: fallback;
}
