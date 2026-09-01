using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Redis.Client.Registrars;
using Soenneker.Redis.Dump.Abstract;
using Soenneker.Utils.File.Registrars;

namespace Soenneker.Redis.Dump.Registrars;

/// <summary>
/// Registers Redis export and import services.
/// </summary>
public static class RedisDumpUtilRegistrar
{
    /// <summary>
    /// Adds <see cref="IRedisDumpUtil"/> and its backing Redis client as singleton services.
    /// </summary>
    /// <param name="services">Service collection that receives the registration.</param>
    /// <returns>The same service collection, so additional registrations can be chained.</returns>
    public static IServiceCollection AddRedisDumpUtilAsSingleton(this IServiceCollection services)
    {
        services.AddRedisClientAsSingleton()
                .AddFileUtilAsSingleton()
                .TryAddSingleton<IRedisDumpUtil, RedisDumpUtil>();

        return services;
    }

    /// <summary>
    /// Adds a scoped <see cref="IRedisDumpUtil"/> backed by a singleton Redis client.
    /// </summary>
    /// <param name="services">Service collection that receives the registration.</param>
    /// <returns>The same service collection, so additional registrations can be chained.</returns>
    public static IServiceCollection AddRedisDumpUtilAsScoped(this IServiceCollection services)
    {
        services.AddRedisClientAsSingleton()
                .AddFileUtilAsScoped()
                .TryAddScoped<IRedisDumpUtil, RedisDumpUtil>();

        return services;
    }
}
