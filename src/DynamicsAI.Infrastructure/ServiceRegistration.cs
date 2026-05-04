using DynamicsAI.Application.Interfaces;
using DynamicsAI.Infrastructure.Cache;
using DynamicsAI.Infrastructure.Dynamics;
using DynamicsAI.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicsAI.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Cache: Redis bağlantı dizisi varsa distributed cache, yoksa in-memory
        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);
            services.AddSingleton<ICacheService, DistributedCacheService>();
        }
        else
        {
            services.AddMemoryCache();
            services.AddSingleton<ICacheService, MetadataCacheService>();
        }

        // Dynamics tenant ID discovery için — 401 yanıtını olduğu gibi alır
        services.AddHttpClient("DynamicsDiscovery")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        // AAD token endpoint için ayrı client
        services.AddHttpClient("DynamicsToken");

        services.AddHttpClient("DynamicsApi")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            })
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(10));

        services.AddSingleton<DynamicsAuthProvider>();
        services.AddScoped<IDynamicsMetadataService, DynamicsMetadataService>();
        services.AddScoped<IDynamicsQueryService, DynamicsQueryService>();
        services.AddScoped<IDynamicsCrudService, DynamicsCrudService>();
        services.AddScoped<IDynamicsExportService, DynamicsExportService>();
        services.AddSingleton<AuditLogger>();

        return services;
    }
}
