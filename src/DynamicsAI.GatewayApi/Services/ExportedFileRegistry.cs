using System.Collections.Concurrent;
using DynamicsAI.GatewayApi.Data;
using DynamicsAI.GatewayApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DynamicsAI.GatewayApi.Services;

public class ExportedFileRegistry(IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<string, (string Path, string Category)> _cache = new();

    public async Task<string> RegisterAsync(string filePath, string category = "export")
    {
        var fileId = Guid.NewGuid().ToString("N");
        _cache[fileId] = (filePath, category);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ExportedFiles.Add(new ExportedFileEntity
        {
            Id        = fileId,
            FilePath  = filePath,
            Category  = category,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    public async Task<string?> GetFilePathAsync(string fileId)
    {
        if (_cache.TryGetValue(fileId, out var cached))
            return cached.Path;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await db.ExportedFiles.FindAsync(fileId);
        if (entity is null) return null;

        _cache[fileId] = (entity.FilePath, entity.Category);
        return entity.FilePath;
    }

    public async Task<IReadOnlyList<ExportedFileEntity>> ListAsync(string? category = null)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.ExportedFiles.AsQueryable();
        if (category is not null)
            query = query.Where(f => f.Category == category);

        return await query.OrderByDescending(f => f.CreatedAt).ToListAsync();
    }
}
