namespace DynamicsAI.Domain.Models;

public class FieldSchema
{
    public required string LogicalName { get; init; }
    public required string DisplayName { get; init; }
    public required string FieldType { get; init; }
    public bool IsRequired { get; init; }
}
