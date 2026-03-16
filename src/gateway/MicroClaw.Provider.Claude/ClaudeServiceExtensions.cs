using Anthropic.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Provider.Claude;

public static class ClaudeServiceExtensions
{
    public const string ServiceKey = "claude";

    public static IServiceCollection AddClaudeChatClient(
        this IServiceCollection services,
        IConfiguration config)
    {
        var apiKey = config["Providers:Claude:ApiKey"] ?? string.Empty;
        var modelId = config["Providers:Claude:ModelId"] ?? "claude-opus-4-5";

        services.AddKeyedSingleton<IChatClient>(ServiceKey, (sp, _) =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new ChatClientBuilder(new AnthropicClient(apiKey).Messages)
                .UseLogging(loggerFactory)
                .UseOpenTelemetry(configure: o => o.EnableSensitiveData = false)
                .Build();
        });

        return services;
    }
}
