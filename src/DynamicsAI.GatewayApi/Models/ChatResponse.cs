using System.Text.Json.Serialization;

namespace DynamicsAI.GatewayApi.Models;

public class ChatResponse
{
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("tool_calls_made")]
    public int ToolCallsMade { get; init; }
}
