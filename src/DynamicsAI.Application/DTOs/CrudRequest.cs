using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicsAI.Application.DTOs;

public class CrudRequest
{
    [JsonPropertyName("tenant_context")]
    public TenantContext? TenantContext { get; init; }

    [JsonPropertyName("operation")]
    public required CrudOperation Operation { get; init; }

    [JsonPropertyName("entity_plural_name")]
    public required string EntityPluralName { get; init; }

    [JsonPropertyName("record_id")]
    public string? RecordId { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CrudOperation
{
    Create,
    Update,
    Delete
}
