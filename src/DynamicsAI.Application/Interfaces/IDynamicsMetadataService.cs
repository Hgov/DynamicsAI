using DynamicsAI.Domain.Models;

namespace DynamicsAI.Application.Interfaces;

public interface IDynamicsMetadataService
{
    Task<IReadOnlyList<EntitySchema>> GetEntitySchemasAsync(TenantConfig tenant, CancellationToken ct = default);
}
