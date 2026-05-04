using DynamicsAI.Application.DTOs;
using DynamicsAI.Domain.Models;

namespace DynamicsAI.Application.Interfaces;

public interface IDynamicsCrudService
{
    Task<CrudResult> ExecuteAsync(TenantConfig tenant, CrudRequest request, CancellationToken ct = default);
}
