using System.Text.Json.Serialization;

namespace DynamicsAI.Application.DTOs;

public class ExportResult
{
    [JsonPropertyName("file_path")]
    public required string FilePath { get; init; }

    [JsonPropertyName("total_records")]
    public long TotalRecords { get; init; }

    [JsonPropertyName("elapsed_seconds")]
    public double ElapsedSeconds { get; init; }
}
