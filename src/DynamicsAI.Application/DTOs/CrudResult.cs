namespace DynamicsAI.Application.DTOs;

public class CrudResult
{
    public bool Success { get; init; }
    public string? RecordId { get; init; }
    public string? Message { get; init; }

    public static CrudResult Created(string recordId) =>
        new() { Success = true, RecordId = recordId, Message = "Record created." };

    public static CrudResult Updated(string recordId) =>
        new() { Success = true, RecordId = recordId, Message = "Record updated." };

    public static CrudResult Deleted(string recordId) =>
        new() { Success = true, RecordId = recordId, Message = "Record deleted." };
}
