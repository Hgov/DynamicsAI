using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.UseCases;
using DynamicsAI.Infrastructure.Logging;
using ModelContextProtocol.Server;

namespace DynamicsAI.McpServer.Tools;

[McpServerToolType]
public static class CrudTool
{
    [McpServerTool(Name = "dynamics_execute_crud")]
    [Description(
        "Creates, updates, or deletes a record in a Dynamics 365 entity. " +
        "IMPORTANT: The server has a pre-configured default tenant — do NOT ask the user for credentials. " +
        "Use dynamics_get_metadata first to discover field names, then call this tool with operation, entity_plural_name, and payload.")]
    public static async Task<string> ExecuteCrud(
        ExecuteCrudUseCase useCase,
        DefaultTenantOptions defaultTenant,
        AuditLogger auditLogger,
        [Description(
            "CRUD parameters: operation (Create/Update/Delete), entity_plural_name, optional record_id for Update/Delete, payload with field values. " +
            "Leave tenant_context null — server uses pre-configured default tenant automatically."
        )] CrudRequest request,
        CancellationToken cancellationToken = default)
    {
        var effectiveRequest = request.TenantContext is not null
            ? request
            : ResolveTenant(request, defaultTenant);

        var tenantId = effectiveRequest.TenantContext?.TenantId ?? effectiveRequest.TenantContext?.DynamicsUrl ?? "default";
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await useCase.ExecuteAsync(effectiveRequest, cancellationToken);
            auditLogger.LogToolCall(tenantId, "dynamics_execute_crud", sw.ElapsedMilliseconds, true);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        }
        catch
        {
            auditLogger.LogToolCall(tenantId, "dynamics_execute_crud", sw.ElapsedMilliseconds, false);
            throw;
        }
    }

    private static CrudRequest ResolveTenant(CrudRequest request, DefaultTenantOptions opts)
    {
        if (!opts.IsConfigured)
            throw new InvalidOperationException(
                "tenant_context sağlanmadı ve sunucuda varsayılan tenant yapılandırılmamış.");

        return new CrudRequest
        {
            TenantContext = opts.ToTenantContext(),
            Operation = request.Operation,
            EntityPluralName = request.EntityPluralName,
            RecordId = request.RecordId,
            Payload = request.Payload
        };
    }
}
