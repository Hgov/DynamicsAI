using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.UseCases;
using DynamicsAI.Infrastructure.Logging;
using ModelContextProtocol.Server;

namespace DynamicsAI.McpServer.Tools;

[McpServerToolType]
public static class QueryTool
{
    [McpServerTool(Name = "dynamics_execute_query")]
    [Description(
        "Lists or views records from a Dynamics 365 entity. " +
        "USE THIS when the user wants to see, list, search, or retrieve records — regardless of count. " +
        "Always set 'top' to limit results (e.g. top=50 for preview, top=1000 for larger sets). " +
        "Do NOT use dynamics_export_to_excel unless the user explicitly asks for a file/download/export. " +
        "IMPORTANT: Server has a pre-configured default tenant — do NOT ask for credentials.")]
    public static async Task<string> ExecuteQuery(
        ExecuteQueryUseCase useCase,
        DefaultTenantOptions defaultTenant,
        AuditLogger auditLogger,
        [Description(
            "Query parameters. Set entity_plural_name (e.g. 'accounts', 'contacts'), " +
            "select_fields, optional filter/order_by/top. " +
            "Leave tenant_context null — server uses the configured default tenant automatically."
        )] QueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var effectiveRequest = ResolveRequest(request, defaultTenant);
        var tenantId = effectiveRequest.TenantContext?.TenantId ?? effectiveRequest.TenantContext?.DynamicsUrl ?? "default";
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await useCase.ExecuteAsync(effectiveRequest, cancellationToken);
            auditLogger.LogToolCall(tenantId, "dynamics_execute_query", sw.ElapsedMilliseconds, true);
            return JsonSerializer.Serialize(result, JsonOptions.Default);
        }
        catch
        {
            auditLogger.LogToolCall(tenantId, "dynamics_execute_query", sw.ElapsedMilliseconds, false);
            throw;
        }
    }

    [McpServerTool(Name = "dynamics_get_count")]
    [Description(
        "Returns the number of records in a Dynamics 365 entity, optionally filtered. " +
        "IMPORTANT: The server has a pre-configured default tenant — do NOT ask the user for credentials. " +
        "Simply call this tool with the entity name and optional filter.")]
    public static async Task<string> GetCount(
        ExecuteQueryUseCase useCase,
        DefaultTenantOptions defaultTenant,
        AuditLogger auditLogger,
        [Description("Plural entity name, e.g. 'accounts', 'contacts', 'leads'")] string entityPluralName,
        [Description("OData filter expression, e.g. \"statecode eq 0\". Leave empty for total count.")] string? filter = null,
        [Description("Only provide if user explicitly wants a DIFFERENT Dynamics environment. Otherwise leave null — server uses pre-configured default.")] TenantContext? tenantContext = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = ResolveContext(tenantContext, defaultTenant);
        var tenantId = ctx.TenantId ?? ctx.DynamicsUrl;
        var sw = Stopwatch.StartNew();
        try
        {
            var count = await useCase.GetCountAsync(ctx, entityPluralName, filter, cancellationToken);
            auditLogger.LogToolCall(tenantId, "dynamics_get_count", sw.ElapsedMilliseconds, true);
            return JsonSerializer.Serialize(new { entity = entityPluralName, count }, JsonOptions.Default);
        }
        catch
        {
            auditLogger.LogToolCall(tenantId, "dynamics_get_count", sw.ElapsedMilliseconds, false);
            throw;
        }
    }

    private static QueryRequest ResolveRequest(QueryRequest request, DefaultTenantOptions opts)
    {
        if (request.TenantContext is not null) return request;

        if (!opts.IsConfigured)
            throw new InvalidOperationException(
                "tenant_context sağlanmadı ve sunucuda varsayılan tenant yapılandırılmamış. " +
                "appsettings.json > DynamicsAI:DefaultTenant bölümünü doldurun.");

        return new QueryRequest
        {
            TenantContext = opts.ToTenantContext(),
            EntityPluralName = request.EntityPluralName,
            SelectFields = request.SelectFields,
            Filter = request.Filter,
            OrderBy = request.OrderBy,
            Top = request.Top,
            Count = request.Count
        };
    }

    private static TenantContext ResolveContext(TenantContext? provided, DefaultTenantOptions opts)
    {
        if (provided is not null) return provided;

        if (!opts.IsConfigured)
            throw new InvalidOperationException(
                "tenant_context sağlanmadı ve sunucuda varsayılan tenant yapılandırılmamış.");

        return opts.ToTenantContext();
    }
}
