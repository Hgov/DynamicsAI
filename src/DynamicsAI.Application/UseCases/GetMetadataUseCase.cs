using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.Interfaces;
using DynamicsAI.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DynamicsAI.Application.UseCases;

public class GetMetadataUseCase(
    IDynamicsMetadataService metadataService,
    ICacheService cache,
    ILogger<GetMetadataUseCase> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(60);

    public async Task<EntityMetadata> ExecuteAsync(TenantContext tenantContext, bool forceRefresh, CancellationToken ct = default)
    {
        var cacheKey = $"metadata:{tenantContext.TenantId}";

        if (!forceRefresh)
        {
            var cached = await cache.GetAsync<EntityMetadata>(cacheKey);
            if (cached is not null)
            {
                logger.LogDebug("Metadata cache HIT for tenant {TenantId}", tenantContext.TenantId);
                return cached;
            }
        }

        logger.LogInformation("Fetching metadata from Dynamics for tenant {TenantId}", tenantContext.TenantId);
        var schemas = await metadataService.GetEntitySchemasAsync(tenantContext.ToTenantConfig(), ct);

        var result = new EntityMetadata { Entities = schemas, FromCache = false };
        await cache.SetAsync(cacheKey, result, CacheTtl);

        return result;
    }
}
