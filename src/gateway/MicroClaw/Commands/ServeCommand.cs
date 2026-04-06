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
using MicroClaw.Channels.Feishu;
using MicroClaw.Channels.WeChat;
using MicroClaw.Channels.WeCom;
using MicroClaw.Tools;
using Microsoft.AspNetCore.StaticFiles;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Skills;
using MicroClaw.Endpoints;
using MicroClaw.Abstractions;
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
		DotEnvLoader.Load();

		var webRootPath = MicroClawConfig.Env.Get(MICROCLAW_WEBUI_PATH);
		var options = new WebApplicationOptions
		{
			WebRootPath = Directory.Exists(webRootPath) ? webRootPath : null
		};

		var builder = WebApplication.CreateBuilder(options);
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
		MigrateDatabase(app);
		SeedDefaultAgent(app);
		EnsureWebChannel(app);
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
		string sessionsDir = MicroClawConfig.Env.SessionsDir;
		string configDir = MicroClawConfig.Env.ConfigDir;
		builder.Services.AddDbContextFactory<GatewayDbContext>(opts =>
		{
			opts.UseSqlite($"Data Source={dbPath}");
			opts.LogTo(_ => {}, LogLevel.None);  // 禁用所有 EF 日志
		});

		builder.Services.AddSingleton<ConfigService>();
		builder.Services.AddSingleton<ProviderConfigStore>(_ => new ProviderConfigStore());
		builder.Services.AddSingleton<SessionStore>(_ => new SessionStore(sessionsDir));
		builder.Services.AddSingleton<ISessionRepository>(sp => sp.GetRequiredService<SessionStore>());
		builder.Services.AddSingleton<IChannelSessionService, ChannelSessionService>();

		builder.Services.AddSingleton<IModelProvider, OpenAIModelProvider>();
		builder.Services.AddSingleton<IModelProvider, AnthropicModelProvider>();
		builder.Services.AddSingleton<ProviderClientFactory>();

		// Agent 服务
		string workspaceRoot = MicroClawConfig.Env.WorkspaceRoot;
		string agentsDir = MicroClawConfig.Env.AgentsDir;
		builder.Services.AddSingleton<AgentStore>(_ => new AgentStore());
		builder.Services.AddSingleton<IPluginAgentRegistrar>(sp => sp.GetRequiredService<AgentStore>());
		builder.Services.AddSingleton<IAgentRepository>(sp => sp.GetRequiredService<AgentStore>());
		builder.Services.AddSingleton<AgentDnaService>(_ => new AgentDnaService(agentsDir));
		builder.Services.AddSingleton<SessionDnaService>(_ => new SessionDnaService(sessionsDir));
		builder.Services.AddSingleton<MemoryService>(_ => new MemoryService(sessionsDir));
		// RAG 服务
		builder.Services.AddSingleton<IEmbeddingProvider, OpenAIEmbeddingProvider>();
		builder.Services.AddSingleton<ProviderEmbeddingFactory>();
		builder.Services.AddSingleton<RagDbContextFactory>(_ => new RagDbContextFactory(workspaceRoot));
		// EmbeddingProviderAccessor 每次调用时实时读取 DB，支持运行时热切换 Embedding Provider
		builder.Services.AddSingleton<IEmbeddingProviderAccessor, EmbeddingProviderAccessor>();
		builder.Services.AddSingleton<IEmbeddingService, DynamicEmbeddingService>();
		builder.Services.AddSingleton<HybridSearchService>();
		var ragOptions = MicroClawConfig.Get<RagOptions>();
		builder.Services.AddSingleton<IRagPruner>(sp => new RagPruner(
			sp.GetRequiredService<RagDbContextFactory>(),
			sp.GetRequiredService<ILogger<RagPruner>>(),
			ragOptions.MaxStorageSizeMb,
			ragOptions.PruneTargetPercent));
		builder.Services.AddSingleton<IRagService>(sp => new RagService(
			sp.GetRequiredService<IEmbeddingService>(),
			sp.GetRequiredService<RagDbContextFactory>(),
			sp.GetRequiredService<HybridSearchService>(),
			sp.GetRequiredService<IDbContextFactory<GatewayDbContext>>(),
			sp.GetRequiredService<IRagPruner>()));
		builder.Services.AddSingleton<RagReindexJobTracker>();
		builder.Services.AddSingleton<RagReindexService>();
		builder.Services.AddSingleton<RagRetrievalContext>();
		builder.Services.AddSingleton<IRagUsageAuditor, RagUsageAuditor>();
		builder.Services.AddSingleton<IContextOverflowSummarizer, ContextOverflowSummarizer>();
		builder.Services.AddSingleton<ISessionMessageRemover>(sp => sp.GetRequiredService<SessionStore>());
		// Pet 情绪系统服务（基于 Session 隔离，替代原 Agent 级 Emotion 系统）
		builder.Services.AddSingleton<IEmotionStore>(_ => new EmotionStore(MicroClawConfig.Env));
		builder.Services.AddSingleton<IEmotionRuleEngine>(_ => new EmotionRuleEngine(new EmotionRuleEngineOptions()));
		builder.Services.AddSingleton<IEmotionBehaviorMapper>(_ => new EmotionBehaviorMapper(new EmotionBehaviorMapperOptions()));
		// 安全/痛觉系统服务
		builder.Services.AddSingleton<IPainMemoryStore, PainMemoryStore>();
		builder.Services.AddSingleton<IToolRiskRegistry>(_ => new DefaultToolRiskRegistry());
		// 白名单/灰名单配置：从 YAML safety 节读取（字段不存在则返回空列表，等同于不配置）
		builder.Services.AddSingleton<IToolListConfig>(sp =>
		{
			var config = sp.GetRequiredService<IConfiguration>();
			List<string> whitelist = config.GetSection("safety:tool-whitelist").Get<List<string>>() ?? [];
			List<string> graylist  = config.GetSection("safety:tool-graylist").Get<List<string>>() ?? [];
			return new ToolListConfig(whitelist, graylist);
		});
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
		// 使用工厂注册 ISubAgentRunner，通过 Lazy<AgentRunner> 打破循环依赖
		builder.Services.AddSingleton<ISubAgentRunner>(sp => new SubAgentRunnerService(
			sp.GetRequiredService<ISessionRepository>(),
			sp.GetRequiredService<AgentStore>(),
			new Lazy<AgentRunner>(() => sp.GetRequiredService<AgentRunner>()),
			MicroClawConfig.Get<AgentsOptions>().SubAgentMaxDepth));
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
		builder.Services.AddSingleton<AgentRunner>();
		// P-F-5: Pet 编排层服务注册（Pet 为消息入口，AgentRunner 保留但不再作为消息入口）
		builder.Services.AddSingleton<MicroClaw.Pet.Storage.PetStateStore>(sp =>
			new MicroClaw.Pet.Storage.PetStateStore(MicroClawConfig.Env));
		builder.Services.AddSingleton<MicroClaw.Pet.RateLimit.PetRateLimiter>();
		builder.Services.AddSingleton<MicroClaw.Pet.Decision.PetModelSelector>();
		builder.Services.AddSingleton<MicroClaw.Pet.Decision.PetDecisionEngine>();
		builder.Services.AddPetStates();
		builder.Services.AddSingleton<MicroClaw.Pet.StateMachine.PetStateMachine>();
		builder.Services.AddSingleton<MicroClaw.Pet.StateMachine.PetSelfAwarenessReportBuilder>();
		builder.Services.AddSingleton<MicroClaw.Pet.Prompt.PetPromptStore>(sp =>
			new MicroClaw.Pet.Prompt.PetPromptStore(MicroClawConfig.Env));
		builder.Services.AddSingleton<MicroClaw.Pet.Prompt.PetPromptEvolver>(sp =>
			new MicroClaw.Pet.Prompt.PetPromptEvolver(
				sp.GetRequiredService<MicroClaw.Pet.Prompt.PetPromptStore>(),
				sp.GetRequiredService<MicroClaw.Pet.Storage.PetStateStore>(),
				sp.GetRequiredService<MicroClaw.Pet.RateLimit.PetRateLimiter>(),
				sp.GetRequiredService<MicroClaw.Pet.Decision.PetModelSelector>(),
				sp.GetRequiredService<MicroClaw.Providers.ProviderClientFactory>(),
				MicroClawConfig.Env,
				sp.GetRequiredService<ILogger<MicroClaw.Pet.Prompt.PetPromptEvolver>>()));
		builder.Services.AddSingleton<MicroClaw.Pet.PetContextFactory>();
		builder.Services.AddSingleton<MicroClaw.Pet.PetFactory>(sp =>
			new MicroClaw.Pet.PetFactory(
				sp.GetRequiredService<MicroClaw.Pet.Storage.PetStateStore>(),
				sp.GetRequiredService<MicroClaw.Pet.PetContextFactory>(),
				sp.GetRequiredService<ISessionRepository>(),
				MicroClawConfig.Env,
				sp.GetRequiredService<ILogger<MicroClaw.Pet.PetFactory>>()));
		builder.Services.AddSingleton<MicroClaw.Pet.Observer.PetSessionObserver>(sp =>
			new MicroClaw.Pet.Observer.PetSessionObserver(
				MicroClawConfig.Env,
				sp.GetRequiredService<ILogger<MicroClaw.Pet.Observer.PetSessionObserver>>()));
		builder.Services.AddSingleton<MicroClaw.Pet.PetRunner>();
		builder.Services.AddSingleton<IPetRunner>(sp => sp.GetRequiredService<MicroClaw.Pet.PetRunner>());
		// P-F-3: IAgentMessageHandler 指向 PetRunner，渠道消息经 Pet 编排后再委派 AgentRunner
		builder.Services.AddSingleton<IAgentMessageHandler>(sp => sp.GetRequiredService<MicroClaw.Pet.PetRunner>());

		// Workflow 服务
		builder.Services.AddSingleton<MicroClaw.Agent.Workflows.WorkflowStore>(_ => new MicroClaw.Agent.Workflows.WorkflowStore(MicroClawConfig.Env.ConfigDir));
		builder.Services.AddSingleton<MicroClaw.Agent.Workflows.WorkflowEngine>();

		// 开发调试指标服务（始终注册；调试端点仅在 Development 环境映射）
		builder.Services.AddSingleton<IDevMetricsService, DevMetricsService>();

		// Skills 服务
		builder.Services.AddSingleton<SkillService>(_ =>
		{
			var skillOpts = MicroClawConfig.Get<SkillOptions>();

			// 解析技能文件夹路径：相对路径以 workspaceRoot 为基准，绝对路径直接使用
			static string ResolveFolder(string folder, string wsRoot) =>
				Path.IsPathRooted(folder)
					? Path.GetFullPath(folder)
					: Path.GetFullPath(Path.Combine(wsRoot, folder));

			var roots = new List<string>
			{
				ResolveFolder(skillOpts.DefaultFolder, workspaceRoot)
			};
			foreach (string extra in skillOpts.AdditionalFolders)
				roots.Add(ResolveFolder(extra, workspaceRoot));
			
				return new SkillService(workspaceRoot, roots);
		});
		builder.Services.AddSingleton<IPluginSkillRegistrar>(sp => sp.GetRequiredService<SkillService>());
		builder.Services.AddSingleton<SkillStore>(sp => new SkillStore(
			sp.GetRequiredService<SkillService>()));
		builder.Services.AddSingleton<SkillToolFactory>(sp => new SkillToolFactory(
			sp.GetRequiredService<SkillStore>(),
			sp.GetRequiredService<SkillService>()));
		builder.Services.AddSingleton<MicroClaw.Skills.SkillInvocationTool>(sp => new MicroClaw.Skills.SkillInvocationTool(
			sp.GetRequiredService<SkillToolFactory>(),
			sp.GetRequiredService<SkillService>(),
			sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
				.CreateLogger<MicroClaw.Skills.SkillInvocationTool>(),
			sp.GetService<ISubAgentRunner>(),
			sp.GetService<MicroClaw.Skills.IAgentLookup>()));
		builder.Services.AddSingleton<MicroClaw.Skills.IAgentLookup, MicroClaw.Services.AgentStoreAgentLookup>();
		builder.Services.AddSingleton<McpServerConfigStore>(_ => new McpServerConfigStore(MicroClawConfig.Env.ConfigDir));

		// D-6: MCP 动态工具注册——运行时注册表，启动时从 DB 同步，API 变更后即时生效，无需重启
		builder.Services.AddSingleton<McpServerRegistry>();
		builder.Services.AddSingleton<IMcpServerRegistry>(sp => sp.GetRequiredService<McpServerRegistry>());
		builder.Services.AddSingleton<IPluginMcpRegistrar>(sp => sp.GetRequiredService<McpServerRegistry>());
		builder.Services.AddHostedService(sp => sp.GetRequiredService<McpServerRegistry>());

		// 工具提供者（实现 IToolProvider，ToolCollector 自动发现，无需手动硬编码）
		builder.Services.AddSingleton<IToolProvider, FetchToolProvider>();
		builder.Services.AddSingleton<IToolProvider, ShellToolProvider>();
		builder.Services.AddSingleton<IToolProvider, CronToolProvider>();
		builder.Services.AddSingleton<IToolProvider, SubAgentToolProvider>();
		builder.Services.AddSingleton<IToolProvider>(sp =>
		{
			var tokenSvc = sp.GetRequiredService<MicroClaw.Services.SandboxTokenService>();
			return new FileToolProvider(sessionsDir,
				(sessionId, relPath) => tokenSvc.GenerateDownloadUrl(sessionId, relPath));
		});
		builder.Services.AddSingleton<IToolProvider, SkillToolProvider>();
		builder.Services.AddSingleton<ToolCollector>();

		// 插件系统
		builder.Services.AddSingleton<PluginLoader>();
		builder.Services.AddSingleton<IPluginRegistry>(sp => sp.GetRequiredService<PluginLoader>());
		builder.Services.AddHostedService(sp => sp.GetRequiredService<PluginLoader>());
		builder.Services.AddSingleton<IHookExecutor, HookExecutor>();

		// 插件市场
		builder.Services.AddSingleton<IPluginMarketplace, ClaudeMarketplaceAdapter>();
		builder.Services.AddSingleton<IPluginMarketplace, CopilotMarketplaceAdapter>();
		builder.Services.AddSingleton<MarketplaceManager>();
		builder.Services.AddSingleton<IMarketplaceManager>(sp => sp.GetRequiredService<MarketplaceManager>());
		builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketplaceManager>());

		// Quartz.NET 定时任务调度
		builder.Services.AddQuartz(q => q.AddJob<SystemJobRunner>(opts => opts.StoreDurably()));
		builder.Services.AddQuartzHostedService(opt =>
		{
			opt.WaitForJobsToComplete = true;
		});
		builder.Services.AddSingleton<CronJobStore>();
		builder.Services.AddSingleton<SessionChatService>();
		builder.Services.AddSingleton<CronJobScheduler>();
		builder.Services.AddSingleton<ICronJobScheduler>(sp => sp.GetRequiredService<CronJobScheduler>());
		builder.Services.AddHostedService<CronJobStartupService>();

		// Token 用量追踪
		builder.Services.AddSingleton<IUsageTracker, UsageTracker>();

		// F-D-1: 渠道消息失败重试队列
		builder.Services.AddSingleton<ChannelRetryQueueService>();
		builder.Services.AddSingleton<IChannelRetryQueue>(sp => sp.GetRequiredService<ChannelRetryQueueService>());
		builder.Services.AddSingleton<IScheduledJob, ChannelRetryJob>();
		// P-E-1: Pet 私有 RAG（每 Session 独立的 knowledge.db，供 RagPruneJob 清理使用）
		builder.Services.AddSingleton<MicroClaw.Pet.Rag.PetRagScope>(sp => new MicroClaw.Pet.Rag.PetRagScope(
			sp.GetRequiredService<IEmbeddingService>(),
			MicroClawConfig.Env,
			sp.GetRequiredService<ILogger<MicroClaw.Pet.Rag.PetRagScope>>()));
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
		builder.Services.AddSingleton<ChannelConfigStore>(_ => new ChannelConfigStore(MicroClawConfig.Env.ConfigDir));

		// 飞书：共享消息处理器 + Webhook 渠道 + WebSocket 长连接管理器
		builder.Services.AddSingleton<FeishuTokenCache>();
		builder.Services.AddSingleton<FeishuRateLimiter>();
		builder.Services.AddSingleton<FeishuChannelHealthStore>();
		builder.Services.AddSingleton<FeishuChannelStatsService>();
		builder.Services.AddSingleton<FeishuMessageProcessor>();
		builder.Services.AddSingleton<IChannel, FeishuChannel>();
		// F-F-2: 同时注册为单例（健康检查端点需直接注入）和 IHostedService
		builder.Services.AddSingleton<FeishuWebSocketManager>();
		builder.Services.AddHostedService(sp => sp.GetRequiredService<FeishuWebSocketManager>());
		// F-C-1: 飞书文档读取工具工厂（供 AgentRunner 加载工具，并展示到渠道工具面板）
		builder.Services.AddSingleton<FeishuToolsFactory>();
		builder.Services.AddSingleton<IToolProvider>(sp => sp.GetRequiredService<FeishuToolsFactory>());
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

		builder.Services.AddSingleton<IChannel, WeComChannel>();
		builder.Services.AddSingleton<IChannel, WeChatChannel>();
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
		AgentConfig main = agentStore.EnsureMainAgent();
		var agentDna = scope.ServiceProvider.GetRequiredService<AgentDnaService>();
		agentDna.InitializeAgent(main.Id);
	}

	/// <summary>确保内置 Web Channel 存在（幂等）。</summary>
	private static void EnsureWebChannel(WebApplication app)
	{
		using var scope = app.Services.CreateScope();
		var channelStore = scope.ServiceProvider.GetRequiredService<ChannelConfigStore>();
		channelStore.EnsureWebChannel();
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
