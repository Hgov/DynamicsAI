using DynamicsAI.Application.DTOs;
using DynamicsAI.Domain.Models;

namespace DynamicsAI.Application.Interfaces;

public interface IDynamicsExportService
{
    Task<ExportResult> ExportToExcelAsync(TenantConfig tenant, ExportRequest request, CancellationToken ct = default);
}
