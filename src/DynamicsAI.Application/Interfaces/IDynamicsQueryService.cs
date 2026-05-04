using DynamicsAI.Application.DTOs;
using DynamicsAI.Domain.Models;

namespace DynamicsAI.Application.Interfaces;

public interface IDynamicsQueryService
{
    Task<QueryResult> ExecuteQueryAsync(TenantConfig tenant, QueryRequest request, CancellationToken ct = default);
    Task<QueryResult> FetchPageAsync(TenantConfig tenant, string pageUrl, CancellationToken ct = default);
    Task<int> GetCountAsync(TenantConfig tenant, string entityPluralName, string? filter, CancellationToken ct = default);
}
