using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.UseCases;
using DynamicsAI.Infrastructure.Logging;
using ModelContextProtocol.Server;

namespace DynamicsAI.McpServer.Tools;

[McpServerToolType]
public static class MetadataTool
{
    [McpServerTool(Name = "dynamics_get_metadata")]
    [Description(
        "Fetches all entity and field schemas from Dynamics 365. Use this first to discover entity names, field names, and types before executing queries or CRUD operations. " +
        "IMPORTANT: The server has a pre-configured default tenant — do NOT ask the user for credentials. " +
        "Simply call this tool; tenant_context is optional and defaults to the server's configured environment.")]
    public static async Task<string> GetMetadata(
        GetMetadataUseCase useCase,
        DefaultTenantOptions defaultTenant,
        AuditLogger auditLogger,
        [Description("Only provide if user explicitly wants a DIFFERENT Dynamics environment. Otherwise leave null — server uses pre-configured default.")] TenantContext? tenantContext = null,
        [Description("Set true to bypass cache and re-fetch from Dynamics")] bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var ctx = tenantContext ?? (defaultTenant.IsConfigured
            ? defaultTenant.ToTenantContext()
            : throw new InvalidOperationException(
                "tenant_context sağlanmadı ve sunucuda varsayılan tenant yapılandırılmamış."));

        var tenantId = ctx.TenantId ?? ctx.DynamicsUrl;
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await useCase.ExecuteAsync(ctx, forceRefresh, cancellationToken);
            auditLogger.LogToolCall(tenantId, "dynamics_get_metadata", sw.ElapsedMilliseconds, true);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        }
        catch
        {
            auditLogger.LogToolCall(tenantId, "dynamics_get_metadata", sw.ElapsedMilliseconds, false);
            throw;
        }
    }
}
