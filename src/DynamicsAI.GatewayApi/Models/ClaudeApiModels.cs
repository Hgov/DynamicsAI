using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicsAI.GatewayApi.Models;

public class ClaudeApiResponse
{
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; init; }

    [JsonPropertyName("content")]
    public List<ClaudeContentBlock> Content { get; init; } = [];
}

public class ClaudeContentBlock
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("input")]
    public JsonElement? Input { get; init; }
}
