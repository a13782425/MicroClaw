using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace MicroClaw.Provider.OpenAI;

public static class OpenAIServiceExtensions
{
    public const string ServiceKey = "openai";

    public static IServiceCollection AddOpenAIChatClient(
        this IServiceCollection services,
        IConfiguration config)
    {
        var apiKey = config["Providers:OpenAI:ApiKey"] ?? string.Empty;
        var modelId = config["Providers:OpenAI:ModelId"] ?? "gpt-4o-mini";

        services.AddKeyedSingleton<IChatClient>(ServiceKey, (sp, _) =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new ChatClientBuilder(new ChatClient(modelId, apiKey).AsIChatClient())
                .UseLogging(loggerFactory)
                .UseOpenTelemetry(configure: o => o.EnableSensitiveData = false)
                .Build();
        });

        return services;
    }
}
