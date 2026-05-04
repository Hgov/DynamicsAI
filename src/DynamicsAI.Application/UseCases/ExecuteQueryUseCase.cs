using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DynamicsAI.Application.UseCases;

public class ExecuteQueryUseCase(
    IDynamicsQueryService queryService,
    ILogger<ExecuteQueryUseCase> logger)
{
    public async Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Executing query on {Entity} for tenant {TenantId}",
            request.EntityPluralName,
            request.TenantContext?.TenantId);

        var tenantConfig = (request.TenantContext
            ?? throw new InvalidOperationException("TenantContext is required — resolve before calling use case."))
            .ToTenantConfig();
        return await queryService.ExecuteQueryAsync(tenantConfig, request, ct);
    }

    public async Task<int> GetCountAsync(TenantContext tenantContext, string entityPluralName, string? filter, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Getting count for {Entity} on tenant {TenantId}",
            entityPluralName,
            tenantContext.TenantId);

        return await queryService.GetCountAsync(tenantContext.ToTenantConfig(), entityPluralName, filter, ct);
    }
}
