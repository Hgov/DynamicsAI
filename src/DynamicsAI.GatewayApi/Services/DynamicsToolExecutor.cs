using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicsAI.Application.DTOs;
using DynamicsAI.Application.UseCases;

namespace DynamicsAI.GatewayApi.Services;

public class DynamicsToolExecutor(
    GetMetadataUseCase metadataUseCase,
    ExecuteQueryUseCase queryUseCase,
    ExecuteCrudUseCase crudUseCase,
    ExportToExcelUseCase exportUseCase,
    ExportedFileRegistry fileRegistry,
    IHttpContextAccessor httpContextAccessor,
    DefaultTenantOptions defaultTenant,
    StorageOptions storageOptions,
    ILogger<DynamicsToolExecutor> logger)
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<string> ExecuteAsync(
        string toolName,
        JsonElement input,
        TenantContext? requestContext,
        CancellationToken ct)
    {
        var ctx = requestContext ?? (defaultTenant.IsConfigured
            ? defaultTenant.ToTenantContext()
            : throw new InvalidOperationException(
                "tenant_context sağlanmadı ve varsayılan tenant yapılandırılmamış."));

        logger.LogInformation("Executing tool {Tool} for {Url}", toolName, ctx.DynamicsUrl);

        return toolName switch
        {
            "dynamics_get_metadata"       => await ExecuteGetMetadataAsync(input, ctx, ct),
            "dynamics_get_entity_fields"  => await ExecuteGetEntityFieldsAsync(input, ctx, ct),
            "dynamics_execute_query"      => await ExecuteQueryAsync(input, ctx, ct),
            "dynamics_get_count"          => await ExecuteGetCountAsync(input, ctx, ct),
            "dynamics_execute_crud"       => await ExecuteCrudAsync(input, ctx, ct),
            "dynamics_export_to_excel"    => await ExecuteExportAsync(input, ctx, ct),
            _ => throw new NotSupportedException($"Bilinmeyen araç: {toolName}")
        };
    }

    // Sadece entity listesi döndürür — field detayı yok, token tasarrufu sağlar
    private async Task<string> ExecuteGetMetadataAsync(JsonElement input, TenantContext ctx, CancellationToken ct)
    {
        var forceRefresh = input.ValueKind == JsonValueKind.Object
            && input.TryGetProperty("force_refresh", out var fr)
            && fr.ValueKind == JsonValueKind.True;

        var result = await metadataUseCase.ExecuteAsync(ctx, forceRefresh, ct);

        var compact = result.Entities.Select(e => new
        {
            logical_name   = e.LogicalName,
            display_name   = e.DisplayName,
            plural_name    = e.PluralName
        });

        return JsonSerializer.Serialize(
            new { entity_count = result.Entities.Count, entities = compact },
            Opts);
    }

    // Belirli bir entity'nin field'larını döndürür
    private async Task<string> ExecuteGetEntityFieldsAsync(JsonElement input, TenantContext ctx, CancellationToken ct)
    {
        var logicalName = input.GetProperty("logical_name").GetString()!;

        var result = await metadataUseCase.ExecuteAsync(ctx, false, ct);
        var entity = result.Entities.FirstOrDefault(
            e => e.LogicalName.Equals(logicalName, StringComparison.OrdinalIgnoreCase));

        if (entity is null)
            return JsonSerializer.Serialize(new { error = $"Entity '{logicalName}' bulunamadı." }, Opts);

        return JsonSerializer.Serialize(new
        {
            logical_name  = entity.LogicalName,
            display_name  = entity.DisplayName,
            plural_name   = entity.PluralName,
            field_count   = entity.Fields.Count,
            fields        = entity.Fields
        }, Opts);
    }

    private async Task<string> ExecuteQueryAsync(JsonElement input, TenantContext ctx, CancellationToken ct)
    {
        var entity = input.GetProperty("entity_plural_name").GetString()!;

        var selectFields = input.TryGetProperty("select_fields", out var sf) && sf.ValueKind == JsonValueKind.Array
            ? sf.Deserialize<List<string>>(Opts) ?? []
            : new List<string>();

        var filter  = GetStringOrNull(input, "filter");
        var orderBy = GetStringOrNull(input, "order_by");
        int? top    = input.TryGetProperty("top", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetInt32() : null;

        var req = new QueryRequest
        {
            TenantContext    = ctx,
            EntityPluralName = entity,
            SelectFields     = selectFields,
            Filter           = filter,
            OrderBy          = orderBy,
            Top              = top
        };

        var result = await queryUseCase.ExecuteAsync(req, ct);
        return JsonSerializer.Serialize(result, Opts);
    }

    private async Task<string> ExecuteGetCountAsync(JsonElement input, TenantContext ctx, CancellationToken ct)
    {
        var entity = input.GetProperty("entity_plural_name").GetString()!;
        var filter = GetStringOrNull(input, "filter");
        var count  = await queryUseCase.GetCountAsync(ctx, entity, filter, ct);
        return JsonSerializer.Serialize(new { entity, count }, Opts);
    }

    private async Task<string> ExecuteCrudAsync(JsonElement input, TenantContext ctx, CancellationToken ct)
    {
        var operation = JsonSerializer.Deserialize<CrudOperation>(input.GetProperty("operation").GetRawText(), Opts);
        var entity    = input.GetProperty("entity_plural_name").GetString()!;
        var recordId  = GetStringOrNull(input, "record_id");
        var payload   = input.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object
            ? p : (JsonElement?)null;

        var req = new CrudRequest
        {
            TenantContext    = ctx,
            Operation        = operation,
            EntityPluralName = entity,
            RecordId         = recordId,
            Payload          = payload
        };

        var result = await crudUseCase.ExecuteAsync(req, ct);
        return JsonSerializer.Serialize(result, Opts);
    }

    private async Task<string> ExecuteExportAsync(JsonElement input, TenantContext ctx, CancellationToken ct)
    {
        var entity = input.GetProperty("entity_plural_name").GetString()!;

        var selectFields = input.TryGetProperty("select_fields", out var sf) && sf.ValueKind == JsonValueKind.Array
            ? sf.Deserialize<List<string>>(Opts) ?? []
            : new List<string>();

        var filter  = GetStringOrNull(input, "filter");
        var orderBy = GetStringOrNull(input, "order_by");
        int? maxRecords = input.TryGetProperty("max_records", out var mr) && mr.ValueKind == JsonValueKind.Number
            ? mr.GetInt32() : null;

        // Çıktı yolunu her zaman exports/ klasörüne yönlendir — Claude'un önerdiği yolu görmezden gel
        var timestamp  = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var outputPath = Path.Combine(storageOptions.ExportsPath, $"{entity}_{timestamp}.xlsx");

        var req = new ExportRequest
        {
            TenantContext    = ctx,
            EntityPluralName = entity,
            SelectFields     = selectFields,
            Filter           = filter,
            OrderBy          = orderBy,
            OutputPath       = outputPath,
            MaxRecords       = maxRecords
        };

        var result = await exportUseCase.ExecuteAsync(req, ct);

        var fileId      = await fileRegistry.RegisterAsync(result.FilePath, "export");
        var downloadUrl = BuildDownloadUrl(fileId);

        return JsonSerializer.Serialize(new
        {
            result.FilePath,
            result.TotalRecords,
            result.ElapsedSeconds,
            download_url = downloadUrl
        }, Opts);
    }

    private string BuildDownloadUrl(string fileId)
    {
        var req = httpContextAccessor.HttpContext?.Request;
        if (req is null) return $"/api/files/{fileId}";
        return $"{req.Scheme}://{req.Host}/api/files/{fileId}";
    }

    private static string? GetStringOrNull(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
