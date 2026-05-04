using System.Text.Json;

namespace DynamicsAI.Application.DTOs;

public class QueryResult
{
    public required IReadOnlyList<JsonElement> Records { get; init; }
    public int? TotalCount { get; init; }
    public string? NextLink { get; init; }
}
