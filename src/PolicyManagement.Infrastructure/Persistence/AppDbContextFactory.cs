using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PolicyManagement.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling when no running host is
/// available. The connection string here is only used for generating migrations;
/// a live database is not required at design time.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=PolicyManagement;Trusted_Connection=True;TrustServerCertificate=True;",
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
