using MicroClaw.Core;
using MicroClaw.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MicroClaw.Extensions;

public static class MicroServiceCollectionExtensions
{
    public static IServiceCollection AddMicroEngine(this IServiceCollection services)
    {
        services.AddSingleton<MicroEngine>();
        services.AddSingleton<MicroEngineHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<MicroEngineHostedService>());
        return services;
    }

    public static IServiceCollection AddMicroService<T>(this IServiceCollection services)
        where T : MicroService
    {
        services.AddSingleton<T>();
        services.AddSingleton<MicroService>(sp => sp.GetRequiredService<T>());
        return services;
    }
}