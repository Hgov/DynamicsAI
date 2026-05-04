using System.Text.Json.Serialization;

namespace DynamicsAI.Application.DTOs;

public class QueryRequest
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

    [JsonPropertyName("top")]
    public int? Top { get; init; }

    [JsonPropertyName("count")]
    public bool Count { get; init; }
}
