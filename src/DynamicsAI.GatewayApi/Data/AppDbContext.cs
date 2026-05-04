using DynamicsAI.GatewayApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DynamicsAI.GatewayApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<ExportedFileEntity> ExportedFiles => Set<ExportedFileEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionEntity>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.UserId);
            e.HasMany(s => s.Messages)
             .WithOne(m => m.Session)
             .HasForeignKey(m => m.SessionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MessageEntity>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.SessionId);
        });

        modelBuilder.Entity<ExportedFileEntity>(e =>
        {
            e.HasKey(f => f.Id);
        });
    }
}
