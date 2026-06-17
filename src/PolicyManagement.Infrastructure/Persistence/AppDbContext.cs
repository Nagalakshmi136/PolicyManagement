using Microsoft.EntityFrameworkCore;
using PolicyManagement.Domain.Entities;

namespace PolicyManagement.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Policy Management BFF.
/// All entity mapping is delegated to <c>IEntityTypeConfiguration&lt;T&gt;</c>
/// classes discovered automatically via <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Policy> Policies { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
