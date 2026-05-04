namespace DynamicsAI.Domain.Models;

public class EntitySchema
{
    public required string LogicalName { get; init; }
    public required string DisplayName { get; init; }
    public required string PluralName { get; init; }
    public IReadOnlyList<FieldSchema> Fields { get; init; } = [];
}
