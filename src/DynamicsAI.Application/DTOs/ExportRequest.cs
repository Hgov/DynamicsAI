using System.Text.Json.Serialization;

namespace DynamicsAI.Application.DTOs;

public class ExportRequest
{
    [JsonPropertyName("tenant_context")]
    public TenantContext? TenantContext { get; init; }

    [JsonPropertyName("entity_plural_name")]
    public required string EntityPluralName { get; init; }

    [JsonPropertyName("select_fields")]
    public IReadOnlyList<string> SelectFields { get; init; } = [];

    [JsonPropertyName("filter")]
    public string? Filter { get; init; }

    [JsonPropertyName("order_by")]
    public string? OrderBy { get; init; }

    /// <summary>
    /// Dışa aktarılacak maksimum kayıt sayısı. Belirtilmezse tüm kayıtlar yazılır.
    /// </summary>
    [JsonPropertyName("max_records")]
    public int? MaxRecords { get; init; }

    /// <summary>
    /// Excel dosyasının kaydedileceği tam yol. Belirtilmezse masaüstüne kaydedilir.
    /// </summary>
    [JsonPropertyName("output_path")]
    public string? OutputPath { get; init; }
}
