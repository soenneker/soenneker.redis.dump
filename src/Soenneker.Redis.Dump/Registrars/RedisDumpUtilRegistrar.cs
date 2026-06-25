using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Redis.Client.Registrars;
using Soenneker.Redis.Dump.Abstract;

namespace Soenneker.Redis.Dump.Registrars;

/// <summary>
/// Redis database export, import, and copy utilities for .NET
/// </summary>
public static class RedisDumpUtilRegistrar
{
    /// <summary>
    /// Adds <see cref="IRedisDumpUtil"/> as a singleton service. <para/>
    /// </summary>
    public static IServiceCollection AddRedisDumpUtilAsSingleton(this IServiceCollection services)
    {
        services.AddRedisClientAsSingleton()
                .TryAddSingleton<IRedisDumpUtil, RedisDumpUtil>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="IRedisDumpUtil"/> as a scoped service. <para/>
    /// </summary>
    public static IServiceCollection AddRedisDumpUtilAsScoped(this IServiceCollection services)
    {
        services.AddRedisClientAsSingleton()
                .TryAddScoped<IRedisDumpUtil, RedisDumpUtil>();

        return services;
    }
}
