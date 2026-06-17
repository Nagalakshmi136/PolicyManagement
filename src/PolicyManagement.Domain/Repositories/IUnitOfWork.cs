namespace PolicyManagement.Domain.Repositories;

/// <summary>
/// Unit of Work port. Implemented by <c>UnitOfWork</c> in Infrastructure.
/// Application handlers call <see cref="SaveChangesAsync"/> after all domain
/// mutations are complete to commit the current transaction atomically.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
