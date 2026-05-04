using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DynamicsAI.Application.UseCases;

public class ExportToExcelUseCase(
    IDynamicsExportService exportService,
    ILogger<ExportToExcelUseCase> logger)
{
    public async Task<ExportResult> ExecuteAsync(ExportRequest request, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Excel export başlatıldı: {Entity}, tenant {TenantId}",
            request.EntityPluralName,
            request.TenantContext?.TenantId);

        var tenant = (request.TenantContext
            ?? throw new InvalidOperationException("TenantContext gerekli — use case çağrılmadan önce çözümlenmiş olmalı."))
            .ToTenantConfig();

        return await exportService.ExportToExcelAsync(tenant, request, ct);
    }
}
