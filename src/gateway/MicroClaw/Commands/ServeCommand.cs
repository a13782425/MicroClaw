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
using MicroClaw.Channels.Feishu; // FeishuChannelExtensions.AddFeishuChannel()
using MicroClaw.Channels.WeChat;
using MicroClaw.Channels.WeCom;
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
using MicroClaw.Hubs;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Jobs;
using MicroClaw.Providers;
using MicroClaw.Providers.Claude;
using MicroClaw.Providers.OpenAI;
using MicroClaw.Pet;
using MicroClaw.Pet.Emotion;
using MicroClaw.Safety;
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

		// 初始化静态配置门面（必须在 YAML 加载之后、使用配置之前）
		MicroClawConfig.Initialize(builder.Configuration, MicroClawConfig.Env.ConfigDir);

		ConfigureLogging(builder);
		ConfigureAuth(builder);
		ConfigureServices(builder);
		ConfigureChannels(builder);
		
		var app = builder.Build();

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
			string logFilePath = MicroClawConfig.Env.LogFilePath;
			string consoleTemplate = cfg["serilog:write_to:0:args:output_template"]
				?? "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
			string fileTemplate = cfg["serilog:write_to:1:args:output_template"]
				?? "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

			lc.MinimumLevel.Is(ParseLevel(cfg["serilog:minimum_level:default"], LogEventLevel.Information))
			  .MinimumLevel.Override("Microsoft.AspNetCore",
				  ParseLevel(cfg["serilog:minimum_level:override:microsoft.aspnetcore"], LogEventLevel.Warning))
			  .MinimumLevel.Override("Microsoft.Extensions.AI",
				  ParseLevel(cfg["serilog:minimum_level:override:microsoft.extensions.ai"], LogEventLevel.Debug))
			  .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command",
				  ParseLevel(cfg["serilog:minimum_level:override:microsoft.entityframeworkcore.database.command"], LogEventLevel.Warning))
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

	/// <summary>注册核心基础设施服务：SQLite DbContext、SessionStore、ProviderConfigStore、各 ModelProvider、SignalR 和 Swagger。</summary>
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
		builder.Services.AddSingleton<ProviderConfigStore>();
		builder.Services.AddService<SessionService>();
		builder.Services.MapAs<ISessionService, SessionService>();
		builder.Services.MapAs<ISessionRepository, SessionService>();

		builder.Services.AddSingleton<IModelProvider, OpenAIModelProvider>();
		builder.Services.AddSingleton<IModelProvider, AnthropicModelProvider>();
		builder.Services.AddSingleton<ProviderClientFactory>();
		
		// Agent 服务
		builder.Services.AddService<AgentStore>();
		builder.Services.MapAs<IPluginAgentRegistrar, AgentStore>();
		builder.Services.MapAs<IAgentRepository, AgentStore>();
		builder.Services.AddSingleton<AgentDnaService>();
		builder.Services.AddSingleton<SessionDnaService>();
		builder.Services.AddSingleton<MemoryService>();
		// RAG 服务
		builder.Services.AddSingleton<IEmbeddingProvider, OpenAIEmbeddingProvider>();
		builder.Services.AddSingleton<ProviderEmbeddingFactory>();
		builder.Services.AddSingleton<RagDbContextFactory>();
		// EmbeddingProviderAccessor 每次调用时实时读取 DB，支持运行时热切换 Embedding Provider
		builder.Services.AddSingleton<IEmbeddingProviderAccessor, EmbeddingProviderAccessor>();
		builder.Services.AddSingleton<IEmbeddingService, DynamicEmbeddingService>();
		builder.Services.AddSingleton<HybridSearchService>();
		builder.Services.AddSingleton<IRagPruner, RagPruner>();
		builder.Services.AddSingleton<IRagService, RagService>();
		builder.Services.AddSingleton<RagReindexJobTracker>();
		builder.Services.AddSingleton<RagReindexService>();
		builder.Services.AddSingleton<RagRetrievalContext>();
		builder.Services.AddSingleton<IRagUsageAuditor, RagUsageAuditor>();
		builder.Services.AddSingleton<IContextOverflowSummarizer, ContextOverflowSummarizer>();
		// Pet 情绪系统服务（基于 Session 隔离，替代原 Agent 级 Emotion 系统）
		builder.Services.AddSingleton<IEmotionStore, EmotionStore>();
		builder.Services.AddSingleton<IEmotionRuleEngine, EmotionRuleEngine>();
		builder.Services.AddSingleton<IEmotionBehaviorMapper, EmotionBehaviorMapper>();
		// 安全/痛觉系统服务
		builder.Services.AddSingleton<IPainMemoryStore, PainMemoryStore>();
		builder.Services.AddSingleton<IToolRiskRegistry, DefaultToolRiskRegistry>();
		// 白名单/灰名单配置：构造函数内自动从 IConfiguration 读取
		builder.Services.AddSingleton<IToolListConfig, ToolListConfig>();
		builder.Services.AddSingleton<IToolRiskInterceptor, ListBasedToolRiskInterceptor>();
		// Provider 路由器
		builder.Services.AddSingleton<IProviderRouter, ProviderRouter>();
		// 痛觉-Pet 情绪联动服务（基于 Session 隔离，Pet 版本替代旧 Agent 级 IPainEmotionLinker）
		builder.Services.AddSingleton<IPainEmotionLinker, MicroClaw.Pet.PainEmotionLinker>();
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
		// AIContent→StreamItem 转换管道（Handler + Pipeline）
		builder.Services.AddSingleton<MicroClaw.Agent.Streaming.IAIContentHandler, MicroClaw.Agent.Streaming.Handlers.TextContentHandler>();
		builder.Services.AddSingleton<MicroClaw.Agent.Streaming.IAIContentHandler, MicroClaw.Agent.Streaming.Handlers.DataContentHandler>();
		builder.Services.AddSingleton<MicroClaw.Agent.Streaming.IAIContentHandler, MicroClaw.Agent.Streaming.Handlers.UsageContentHandler>();
		builder.Services.AddSingleton<MicroClaw.Agent.Streaming.IAIContentHandler, MicroClaw.Agent.Streaming.Handlers.ThinkingContentHandler>();
		builder.Services.AddSingleton<MicroClaw.Agent.Streaming.AIContentPipeline>();
		// StreamItem 持久化 Handler（供 SessionEndpoints 注入）
		builder.Services.AddSingleton<MicroClaw.Abstractions.Streaming.IStreamItemPersistenceHandler, MicroClaw.Streaming.PersistenceHandlers.ToolCallPersistenceHandler>();
		builder.Services.AddSingleton<MicroClaw.Abstractions.Streaming.IStreamItemPersistenceHandler, MicroClaw.Streaming.PersistenceHandlers.ToolResultPersistenceHandler>();
		builder.Services.AddSingleton<MicroClaw.Abstractions.Streaming.IStreamItemPersistenceHandler, MicroClaw.Streaming.PersistenceHandlers.SubAgentStartPersistenceHandler>();
		builder.Services.AddSingleton<MicroClaw.Abstractions.Streaming.IStreamItemPersistenceHandler, MicroClaw.Streaming.PersistenceHandlers.SubAgentResultPersistenceHandler>();
		// SessionMessage → AIContent 还原策略（供 BuildChatMessagesAsync 使用）
		builder.Services.AddSingleton<MicroClaw.Agent.Restorers.IChatContentRestorer, MicroClaw.Agent.Restorers.ThinkingContentRestorer>();
		builder.Services.AddSingleton<MicroClaw.Agent.Restorers.IChatContentRestorer, MicroClaw.Agent.Restorers.TextContentRestorer>();
		builder.Services.AddSingleton<MicroClaw.Agent.Restorers.IChatContentRestorer, MicroClaw.Agent.Restorers.FunctionCallRestorer>();
		builder.Services.AddSingleton<MicroClaw.Agent.Restorers.IChatContentRestorer, MicroClaw.Agent.Restorers.FunctionResultRestorer>();
		builder.Services.AddSingleton<MicroClaw.Agent.Restorers.IChatContentRestorer, MicroClaw.Agent.Restorers.DataContentRestorer>();
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
		builder.Services.AddService<MicroClaw.Pet.PetRunner>();
		builder.Services.MapAs<IPetRunner, MicroClaw.Pet.PetRunner>();
		// P-F-3: IAgentMessageHandler 指向 PetRunner，渠道消息经 Pet 编排后再委派 AgentRunner
		builder.Services.MapAs<IAgentMessageHandler, MicroClaw.Pet.PetRunner>();

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
		builder.Services.AddHostedService<SessionRunner>();

		// Token 用量追踪
		builder.Services.AddSingleton<IUsageTracker, UsageTracker>();

		// F-D-1: 渠道消息失败重试队列
		builder.Services.AddSingleton<ChannelRetryQueueService>();
		builder.Services.MapAs<IChannelRetryQueue, ChannelRetryQueueService>();
		builder.Services.AddSingleton<IScheduledJob, ChannelRetryJob>();
		// P-E-1: Pet 私有 RAG（每 Session 独立的 knowledge.db，供 RagPruneJob 清理使用）
		builder.Services.AddSingleton<MicroClaw.Pet.Rag.PetRagScope>();
		// 2-A-11: RAG 定期容量清理（每日 UTC 01:00，早于记忆总结和做梦模式）
		builder.Services.AddSingleton<IScheduledJob, RagPruneJob>();
		// P-B-7: Pet 情绪自然衰减（每小时向默认值 50 靠近，每 Session 独立衰减）
		builder.Services.AddSingleton<IScheduledJob, PetEmotionDecayJob>();
		// P-G: Pet 心跳与自主行为
		builder.Services.AddSingleton<MicroClaw.Pet.Heartbeat.IPetNotifier, MicroClaw.Services.HubPetNotifier>();
		builder.Services.AddSingleton<MicroClaw.Pet.Heartbeat.PetActionExecutor>();
		builder.Services.AddSingleton<MicroClaw.Pet.Heartbeat.PetHeartbeatExecutor>();
		builder.Services.AddSingleton<IScheduledJob, PetHeartbeatJob>();

		// 领域事件基础设施（O-0-3）
		builder.Services.AddSingleton<IDomainEventDispatcher, MicroClaw.Events.DomainEventDispatcher>();

		// 领域事件处理器（O-1-9, O-1-10）
		builder.Services.AddSingleton<IDomainEventHandler<SessionApprovedEvent>, MicroClaw.Events.SessionApprovedEventHandler>();
		builder.Services.AddSingleton<IDomainEventHandler<SessionDeletedEvent>, MicroClaw.Events.SessionDeletedEventHandler>();
	}

	/// <summary>注册渠道配置存储和渠道实现（飞书、企业微信、微信），渠道配置由数据库管理。</summary>
	private static void ConfigureChannels(WebApplicationBuilder builder)
	{
		builder.Services.AddService<ChannelService>();
		builder.Services.MapAs<IChannelService, ChannelService>();

		// 内置渠道 Provider
		builder.Services.AddSingleton<IChannelProvider, WebChannelProvider>();
		builder.Services.AddSingleton<IChannelProvider, WeComChannelProvider>();
		builder.Services.AddSingleton<IChannelProvider, WeChatChannelProvider>();

		// 飞书：封装所有内部实现类的注册，外部不引用具体类型
		builder.Services.AddFeishuChannel();
		// F-C-7: 飞书对话摘要定时同步（将会话消息追加到配置的 summaryDocToken 文档）
		builder.Services.AddSingleton<IScheduledJob, FeishuDocSyncJob>();
		// F-F-3: 飞书 WebSocket 渠道配置 30s 同步（从 FeishuWebSocketManager.PeriodicTimer 提取）
		builder.Services.AddSingleton<IScheduledJob, FeishuWebSocketSyncJob>();
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
		var logger = app.Logger;

		string jwtSecret = MicroClawConfig.Get<AuthOptions>().JwtSecret;
		int jwtSecretBytes = Encoding.UTF8.GetByteCount(jwtSecret);
		if (jwtSecretBytes < 32)
		{
			logger.LogWarning(
				"JWT secret 强度不足（当前 {Bytes} 字节，要求 ≥32 字节）。" +
				"请在配置项 auth:jwt_secret 中设置 32 个字符以上的强密钥，否则存在被暴力破解的安全风险。",
				jwtSecretBytes);
		}
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
}
