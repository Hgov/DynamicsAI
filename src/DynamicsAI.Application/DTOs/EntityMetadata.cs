using DynamicsAI.Domain.Models;

namespace DynamicsAI.Application.DTOs;

public class EntityMetadata
{
    public required IReadOnlyList<EntitySchema> Entities { get; init; }
    public DateTime CachedAt { get; init; } = DateTime.UtcNow;
    public bool FromCache { get; init; }
}
