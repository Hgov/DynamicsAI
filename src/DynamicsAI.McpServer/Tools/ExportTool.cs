using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.UseCases;
using DynamicsAI.Infrastructure.Logging;
using ModelContextProtocol.Server;

namespace DynamicsAI.McpServer.Tools;

[McpServerToolType]
public static class ExportTool
{
    [McpServerTool(Name = "dynamics_export_to_excel")]
    [Description(
        "Exports records from a Dynamics 365 entity to an Excel (.xlsx) file and returns a download link. " +
        "ONLY call this when the user explicitly requests a file, download, or export (keywords: 'excel', 'indir', 'aktar', 'export', 'dosya'). " +
        "Set max_records when the user specifies a count (e.g. '1000 kayıt excel', '500 firma indir'). Omit max_records only when user explicitly wants ALL records. " +
        "Do NOT call this just because the user wants many records — use dynamics_execute_query instead. " +
        "IMPORTANT: Server has a pre-configured default tenant — do NOT ask for credentials.")]
    public static async Task<string> ExportToExcel(
        ExportToExcelUseCase useCase,
        DefaultTenantOptions defaultTenant,
        AuditLogger auditLogger,
        [Description(
            "Export parameters. Set entity_plural_name (e.g. 'accounts', 'contacts'). " +
            "Optionally set select_fields, filter, order_by. " +
            "Optionally set output_path (full file path) — defaults to Desktop if omitted. " +
            "Leave tenant_context null — server uses the configured default tenant."
        )] ExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var effectiveRequest = ResolveRequest(request, defaultTenant);
        var tenantId = effectiveRequest.TenantContext?.TenantId ?? effectiveRequest.TenantContext?.DynamicsUrl ?? "default";
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await useCase.ExecuteAsync(effectiveRequest, cancellationToken);
            auditLogger.LogToolCall(tenantId, "dynamics_export_to_excel", sw.ElapsedMilliseconds, true);

            // file:// URI — Claude Desktop'ta tıklanabilir link olarak görünür
            var fileUri = new Uri(result.FilePath).AbsoluteUri;
            return JsonSerializer.Serialize(new
            {
                result.FilePath,
                result.TotalRecords,
                result.ElapsedSeconds,
                download_url = fileUri
            }, JsonOptions.Default);
        }
        catch
        {
            auditLogger.LogToolCall(tenantId, "dynamics_export_to_excel", sw.ElapsedMilliseconds, false);
            throw;
        }
    }

    private static ExportRequest ResolveRequest(ExportRequest request, DefaultTenantOptions opts)
    {
        if (request.TenantContext is not null) return request;

        if (!opts.IsConfigured)
            throw new InvalidOperationException(
                "tenant_context sağlanmadı ve sunucuda varsayılan tenant yapılandırılmamış.");

        return new ExportRequest
        {
            TenantContext    = opts.ToTenantContext(),
            EntityPluralName = request.EntityPluralName,
            SelectFields     = request.SelectFields,
            Filter           = request.Filter,
            OrderBy          = request.OrderBy,
            MaxRecords       = request.MaxRecords,
            OutputPath       = request.OutputPath
        };
    }
}
