using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DynamicsAI.Application.UseCases;

public class ExecuteCrudUseCase(
    IDynamicsCrudService crudService,
    ILogger<ExecuteCrudUseCase> logger)
{
    public async Task<CrudResult> ExecuteAsync(CrudRequest request, CancellationToken ct = default)
    {
        var tenantConfig = (request.TenantContext
            ?? throw new InvalidOperationException("TenantContext is required — resolve before calling use case."))
            .ToTenantConfig();

        logger.LogInformation(
            "Executing {Operation} on {Entity} for tenant {TenantId}",
            request.Operation,
            request.EntityPluralName,
            tenantConfig.TenantId);

        return await crudService.ExecuteAsync(tenantConfig, request, ct);
    }
}
