using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using DataHub.Application.Common;

namespace DataHub.Infrastructure.Caching;

/// <summary>
/// Provider-agnostic cache binding: a configured Redis connection switches
/// the IDistributedCache to StackExchange.Redis (plus the Redis health
/// check); absent that, the in-memory provider serves demo/tests. The
/// application layer never knows which one answered.
/// </summary>
public static class CacheWiring
{
    public static IServiceCollection AddDataHubCache(this IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration["Cache:Redis:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;
                options.InstanceName = "datahub:";
            });
        }
        else
        {
            services.TryAddSingleton<IDistributedCache, MemoryDistributedCache>();
        }

        services.AddSingleton<ICacheStore, DistributedCacheStore>();

        return services;
    }
}
