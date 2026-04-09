using MicroClaw.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MicroClaw.Extensions;

/// <summary>
/// 服务注册扩展方法，为 <see cref="IServiceCollection"/> 提供语义化注册入口。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 将 <typeparamref name="T"/> 同时注册为 <see cref="ServiceLifetime.Singleton"/> 和 <see cref="IService"/>。
    /// </summary>
    public static IServiceCollection AddService<T>(this IServiceCollection services)
        where T : class, IService
    {
        services.AddSingleton<T>();
        services.AddSingleton<IService>(sp => sp.GetRequiredService<T>());
        return services;
    }

    /// <summary>
    /// 将已注册的 <typeparamref name="TImpl"/> 作为接口 <typeparamref name="TInterface"/> 的别名暴露。
    /// <typeparamref name="TImpl"/> 必须已通过其他方式（如 <see cref="AddService{T}"/> 或 <c>AddSingleton</c>）注册。
    /// </summary>
    public static IServiceCollection MapAs<TInterface, TImpl>(this IServiceCollection services)
        where TInterface : class
        where TImpl : class, TInterface
    {
        services.AddSingleton<TInterface>(sp => sp.GetRequiredService<TImpl>());
        return services;
    }

    /// <summary>
    /// 将 <typeparamref name="T"/> 同时注册为 <see cref="ServiceLifetime.Singleton"/> 和 <see cref="IHostedService"/>。
    /// 用于具有后台循环的 <see cref="BackgroundService"/> 子类。
    /// </summary>
    public static IServiceCollection AddRunner<T>(this IServiceCollection services)
        where T : class, IHostedService
    {
        services.AddSingleton<T>();
        services.AddHostedService(sp => sp.GetRequiredService<T>());
        return services;
    }
}
