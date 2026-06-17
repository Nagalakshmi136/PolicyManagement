using PolicyManagement.Domain.Repositories;

namespace PolicyManagement.Infrastructure.Persistence;

/// <summary>
/// Thin Unit of Work wrapper around <see cref="AppDbContext.SaveChangesAsync"/>.
/// Application handlers call this after all domain mutations are complete to
/// commit the current transaction atomically.
/// Repositories must never call <c>SaveChangesAsync</c> directly.
/// </summary>
internal sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
